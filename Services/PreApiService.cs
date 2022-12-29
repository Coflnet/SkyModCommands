namespace Coflnet.Sky.ModCommands.Services;

using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Commands.Shared;
using System.Collections.Concurrent;
using Coflnet.Sky.Commands;

/// <summary>
/// Handles events before the api update
/// </summary>
public class PreApiService : BackgroundService
{
    ConnectionMultiplexer redis;
    IConfiguration config;
    ILogger<PreApiService> logger;
    private ConcurrentDictionary<IFlipConnection, DateTime> users = new();
    public PreApiService(ConnectionMultiplexer redis, IConfiguration config, FlipperService flipperService, ILogger<PreApiService> logger)
    {
        this.redis = redis;
        this.config = config;
        this.logger = logger;

        flipperService.PreApiLowPriceHandler += PreApiLowPriceHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // here to trigger the creation of the service
        await Task.Delay(1000, stoppingToken);
    }
    public void AddUser(IFlipConnection connection, DateTime expires)
    {
        users.AddOrUpdate(connection, expires, (key, old) => expires);
        logger.LogInformation($"Added user {connection.UserId} to flip list {users.Count} users {expires}");
    }

    private async Task PreApiLowPriceHandler(FlipperService sender, LowPricedAuction e)
    {
        e.Auction.ItemName += Commands.MC.McColorCodes.DARK_GRAY + ".";
        foreach (var item in users)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation($"Sent flip to {item.Key.UserId} for {e.Auction.Uuid} ");
                    await item.Key.SendFlip(e).ConfigureAwait(false);
                    if (item.Value < DateTime.Now)
                    {
                        users.TryRemove(item.Key, out _);
                        logger.LogInformation("Removed user from flip list");
                    }
                }
                catch (System.Exception e)
                {
                    logger.LogError(e, "Error while sending flip to user");
                }
            }).ConfigureAwait(false);
        }
        var profit = e.TargetPrice - e.Auction.StartingBid;
        if (profit > 0)
            logger.LogInformation($"Pre-api low price handler called for {e.Auction.Uuid} profit {profit} users {users.Count}");
        await Task.Delay(20_000).ConfigureAwait(false);
        // check if flip was sent to anyone 
        await Task.Delay(15_000).ConfigureAwait(false);
        // if not send to all users
    }
}