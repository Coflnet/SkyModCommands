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
    public PreApiService(ConnectionMultiplexer redis, IConfiguration config, FlipperService flipperService)
    {
        this.redis = redis;
        this.config = config;

        flipperService.PreApiLowPriceHandler += PreApiLowPriceHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

    }
    public void AddUser(IFlipConnection connection, DateTime expires)
    {
        users.TryAdd(connection, expires);
    }

    private async Task PreApiLowPriceHandler(FlipperService sender, LowPricedAuction e)
    {
        foreach (var item in users)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await item.Key.SendFlip(e);
                    if (item.Value < DateTime.Now)
                        users.TryRemove(item.Key, out _);
                }
                catch (System.Exception e)
                {
                    logger.LogError(e, "Error while sending flip to user");
                }
            });
        }
        logger.LogInformation($"Pre-api low price handler called for {e.Auction.Uuid} profit {e.TargetPrice - e.Auction.StartingBid}");
        await Task.Delay(20_000);
        // check if flip was sent to anyone 
        await Task.Delay(10_000);
        // if not send to all users
    }
}