using System;
using System.Collections.Concurrent;
using Coflnet.Sky.Commands.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Tracks matched flips locally and shares them between instances via redis.
/// </summary>
public class RedisFlipMatchTracker : IFlipMatchTracker
{
    public const string Channel = "flip_matched";
    private readonly InMemoryFlipMatchTracker local = new();
    private readonly IConnectionMultiplexer redis;
    private readonly ILogger<RedisFlipMatchTracker> logger;
    private readonly string serverName;
    private readonly ConcurrentDictionary<ISubscriber, byte> subscribed = new();

    private static readonly Prometheus.Histogram remoteLag = Prometheus.Metrics.CreateHistogram("sky_mod_flip_match_remote_lag_seconds",
        "Seconds between a flip match being published by another server and received here",
        new Prometheus.HistogramConfiguration { Buckets = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.2, 0.3, 0.5, 1] });

    public RedisFlipMatchTracker(IConnectionMultiplexer redis, ILogger<RedisFlipMatchTracker> logger, string serverName = null)
    {
        this.redis = redis;
        this.logger = logger;
        this.serverName = serverName ?? System.Net.Dns.GetHostName();
        if (redis != null)
            SubscribeRemote(redis.GetSubscriber());
    }

    public bool ReportMatch(long auctionUId, AccountTier tier)
    {
        var increased = local.ReportMatch(auctionUId, tier);
        if (!increased || tier < AccountTier.STARTER_PREMIUM || redis == null)
            return increased;
        var payload = MessagePackSerializer.Serialize(new FlipMatch { UId = auctionUId, Tier = tier, Server = serverName, SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        redis.GetSubscriber().Publish(RedisChannel.Literal(Channel), payload, CommandFlags.FireAndForget);
        return true;
    }

    public AccountTier HighestMatchedTier(long auctionUId) => local.HighestMatchedTier(auctionUId);

    public DateTime? ReachedAt(long auctionUId, AccountTier tier) => local.ReachedAt(auctionUId, tier);

    public void SubscribeRemote(ISubscriber subscriber)
    {
        if (!subscribed.TryAdd(subscriber, 0))
            return;
        subscriber.Subscribe(RedisChannel.Literal(Channel), (_, value) => OnRemoteMessage(value));
    }

    private void OnRemoteMessage(RedisValue value)
    {
        try
        {
            var match = MessagePackSerializer.Deserialize<FlipMatch>((byte[])value);
            if (match.Server == serverName)
                return;
            local.ReportMatch(match.UId, match.Tier);
            ObserveLag(match);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not process remote flip match");
        }
    }

    private static void ObserveLag(FlipMatch match)
    {
        var lag = LagSeconds(match, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (lag.HasValue)
            remoteLag.Observe(lag.Value);
    }

    /// <summary>Null for payloads without a send time, negative values (clock skew) are clamped to 0</summary>
    internal static double? LagSeconds(FlipMatch match, long nowUnixMs)
    {
        if (match.SentAtUnixMs <= 0)
            return null;
        return Math.Max(0, (nowUnixMs - match.SentAtUnixMs) / 1000.0);
    }

    [MessagePackObject]
    public class FlipMatch
    {
        [Key(0)]
        public long UId { get; set; }
        [Key(1)]
        public AccountTier Tier { get; set; }
        [Key(2)]
        public string Server { get; set; }
        [Key(3)]
        public long SentAtUnixMs { get; set; }
    }
}
