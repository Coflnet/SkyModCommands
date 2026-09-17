using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using Coflnet.Sky.Commands.MC;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarSignalSubscriptionService : BackgroundService
{
    private readonly IConnectionMultiplexer redis;
    private readonly IConnectionMultiplexer bazaarRedis;
    private readonly IHttpClientFactory clients;
    private readonly ILogger<BazaarSignalSubscriptionService> logger;

    public BazaarSignalSubscriptionService(
        IConnectionMultiplexer redis,
        ILogger<BazaarSignalSubscriptionService> logger,
        [FromKeyedServices("bazaar")] IConnectionMultiplexer bazaarRedis, IHttpClientFactory clients = null)
    {
        this.redis = redis;
        this.logger = logger;
        this.bazaarRedis = bazaarRedis;
        this.clients = clients;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var channel = RedisChannel.Literal(BazaarSignalChannels.LiveSignals);
        await subscriber.SubscribeAsync(channel, (subscriptionChannel, message) =>
        {
            _ = HandleMessageAsync(message);
        });
        ChannelMessageQueue orders = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    orders = await bazaarRedis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(BazaarOrderSnapshot.Channel));
                    break;
                }
                catch (RedisException e)
                {
                    logger.LogWarning(e, "Bazaar order subscription unavailable; retrying in 10 seconds");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            if (orders == null)
                return;
            // A tutorial lookup must not hold up fill pushes for other users.
            orders.OnMessage(message => { _ = HandleOrdersAsync(message.Message); });
            logger.LogInformation("Subscribed to Bazaar signals and order updates");
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
            if (orders != null)
                await orders.UnsubscribeAsync();
        }
    }

    private async Task HandleOrdersAsync(RedisValue message)
    {
        try
        {
            var state = JsonConvert.DeserializeObject<BazaarOrderSnapshot>(message!);
            if (state?.UserId == null)
                return;
            var sockets = MinecraftSocket.GetActiveSockets(state.UserId).ToList();
            logger.LogDebug("Received Bazaar snapshot for {UserId}, revision {Revision}, active sockets {SocketCount}, origin {TraceParent}",
                state.UserId, state.Revision, sockets.Count, state.TraceParent);
            await SendOrdersAsync(sockets, state);
        }
        catch (Exception e) { logger.LogError(e, "Failed to read Bazaar order state"); }
    }

    internal Task SendOrdersAsync(IEnumerable<IMinecraftSocket> sockets, BazaarOrderSnapshot state) =>
        Task.WhenAll(sockets.Select(async socket =>
        {
            // A slow tutorial or failed connection must not block the user's other sessions.
            try { await BazaarOrderDisplay.Apply(socket, state, logger: logger); }
            catch (Exception e) { logger.LogError(e, "Failed to send Bazaar orders for {UserId}, revision {Revision}, origin {TraceParent}", state.UserId, state.Revision, state.TraceParent); }
        }));

    public async Task RestoreAsync(IMinecraftSocket socket)
    {
        if (!BazaarOrderDisplay.Supports(socket))
        {
            logger.LogDebug("Skipped Bazaar restoration for {UserId}: unsupported version {Version}", socket.UserId, socket.Version);
            return;
        }
        using var span = BazaarOrderDisplay.Traces.StartActivity("bazaar.hud.restore");
        span?.SetTag("bazaar.user_id", socket.UserId);
        var source = "cache";
        RedisValue value = RedisValue.Null;
        try { value = await bazaarRedis.GetDatabase().StringGetAsync($"{BazaarOrderSnapshot.Channel}:{socket.UserId}"); }
        catch (RedisException e) { logger.LogWarning(e, "Bazaar cache unavailable for {UserId}; requesting matching service; TraceId {TraceId}", socket.UserId, Activity.Current?.TraceId.ToString()); }
        if (value.IsNullOrEmpty && clients != null)
        {
            source = "authority";
            logger.LogDebug("Requesting Bazaar restoration from authority for {UserId}; TraceId {TraceId}", socket.UserId, Activity.Current?.TraceId.ToString());
            try
            {
                using var response = await clients.CreateClient("BazaarOrders")
                    .GetAsync($"OrderBook/user/{Uri.EscapeDataString(socket.UserId)}");
                if (response.IsSuccessStatusCode)
                    value = await response.Content.ReadAsStringAsync();
                else
                    logger.LogDebug("Bazaar restoration returned {StatusCode} for {UserId}; TraceId {TraceId}", (int)response.StatusCode, socket.UserId, Activity.Current?.TraceId.ToString());
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(e, "Bazaar order restoration unavailable for {UserId}; TraceId {TraceId}", socket.UserId, Activity.Current?.TraceId.ToString());
            }
        }
        logger.LogDebug("Bazaar restoration for {UserId}: source {Source}, found {Found}; TraceId {TraceId}", socket.UserId, source, !value.IsNullOrEmpty, Activity.Current?.TraceId.ToString());
        if (!value.IsNullOrEmpty)
            await BazaarOrderDisplay.Apply(socket, JsonConvert.DeserializeObject<BazaarOrderSnapshot>(value!), restore: true, logger: logger);
        else if (socket.SessionInfo.BazaarDisplayState == null)
            BazaarOrderDisplay.Send(socket);
    }

    private Task HandleMessageAsync(RedisValue message)
    {
        try
        {
            var signal = JsonConvert.DeserializeObject<BazaarSignalEvent>(message!);
            if (signal == null)
            {
                return Task.CompletedTask;
            }

            if (!string.Equals(signal.Type, BazaarSignalTypes.OrderFilled, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(signal.Type, BazaarSignalTypes.InstaSellIntent, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            logger.LogInformation(
                "Received bazaar signal {Type} for {ItemTag} amount {Amount} user {UserId} mcUuid {MinecraftUuid} mcName {MinecraftName} source {Source}",
                signal.Type,
                signal.ItemTag,
                signal.Amount,
                signal.UserId,
                signal.MinecraftUuid,
                signal.MinecraftName,
                signal.Source);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to handle bazaar signal message");
        }

        return Task.CompletedTask;
    }
}