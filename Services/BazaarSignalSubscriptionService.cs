using System;
using System.Threading;
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
    private readonly ILogger<BazaarSignalSubscriptionService> logger;

    public BazaarSignalSubscriptionService(
        IConnectionMultiplexer redis,
        ILogger<BazaarSignalSubscriptionService> logger)
    {
        this.redis = redis;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var channel = RedisChannel.Literal(BazaarSignalChannels.LiveSignals);
        await subscriber.SubscribeAsync(channel, (subscriptionChannel, message) =>
        {
            _ = HandleMessageAsync(message);
        });
        logger.LogInformation("Subscribed to bazaar signals on {Channel}", BazaarSignalChannels.LiveSignals);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
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