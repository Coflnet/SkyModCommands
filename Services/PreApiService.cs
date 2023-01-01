namespace Coflnet.Sky.ModCommands.Services;

using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Commands.Shared;
using System.Collections.Concurrent;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;

/// <summary>
/// Handles events before the api update
/// </summary>
public class PreApiService : BackgroundService
{
    ConnectionMultiplexer redis;
    IConfiguration config;
    ILogger<PreApiService> logger;
    static private ConcurrentDictionary<IFlipConnection, DateTime> localUsers = new();
    Payments.Client.Api.ProductsApi productsApi;
    private List<string> preApiUsers = new();
    public PreApiService(ConnectionMultiplexer redis, IConfiguration config, FlipperService flipperService, ILogger<PreApiService> logger, Payments.Client.Api.ProductsApi productsApi)
    {
        this.redis = redis;
        this.config = config;
        this.logger = logger;

        flipperService.PreApiLowPriceHandler += PreApiLowPriceHandler;
        this.productsApi = productsApi;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // here to trigger the creation of the service
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            foreach (var item in localUsers)
            {
                if (item.Value - TimeSpan.FromMinutes(1) < now && item.Key is IMinecraftSocket socket)
                {
                    socket.Dialog(db => db.CoflCommand<PurchaseCommand>($"Your {McColorCodes.RED}pre api{McColorCodes.WHITE} will expire in {McColorCodes.RED}under one minute{McColorCodes.WHITE}\nClick {McColorCodes.RED}here{McColorCodes.WHITE} to renew", "pre_api",
                        $"{McColorCodes.RED}Starts the purchase for another hour of {McColorCodes.RED}pre api{McColorCodes.WHITE}"));
                    if (item.Value < now)
                        localUsers.TryRemove(item.Key, out _);
                }
            }
            await RefreshUsers();

            await Task.Delay(45000, stoppingToken);
        }
    }

    private async Task RefreshUsers()
    {
        try
        {
            preApiUsers = await productsApi.ProductsServiceServiceSlugIdsGetAsync("pre_api");
        }
        catch (System.Exception e)
        {
            logger.LogError(e, "Failed to get pre api users");
        }
    }

    public void AddUser(IFlipConnection connection, DateTime expires)
    {
        localUsers.AddOrUpdate(connection, expires, (key, old) => expires);
        logger.LogInformation($"Added user {connection.UserId} to flip list {localUsers.Count} users {expires}");
        Task.Run(RefreshUsers);
    }

    private async Task PreApiLowPriceHandler(FlipperService sender, LowPricedAuction e)
    {
        if (e.Auction.Context.ContainsKey("cname"))
            e.Auction.Context["cname"] += McColorCodes.DARK_GRAY + ".";
        foreach (var item in localUsers.Keys)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var index = preApiUsers.IndexOf(item is MinecraftSocket socket ? socket.UserId : "1");
                    var isMyRR = e.Auction.UId % preApiUsers.Count == index;
                    if (!isMyRR)
                        await Task.Delay(Random.Shared.Next(4000, 8000)).ConfigureAwait(false);
                    else if (e.Auction.Context.ContainsKey("cname"))
                        e.Auction.Context["cname"].Replace(McColorCodes.DARK_GRAY + ".", McColorCodes.RED + ".");
                    logger.LogInformation($"Sent flip to {item.UserId} for {e.Auction.Uuid} ");
                    var sendSuccessful = await item.SendFlip(e).ConfigureAwait(false);
                    if (!sendSuccessful)
                    {
                        logger.LogInformation($"Failed to send flip to {item.UserId} for {e.Auction.Uuid}");
                        localUsers.TryRemove(item, out _);
                    }
                    if (localUsers[item] < DateTime.Now)
                    {
                        localUsers.TryRemove(item, out _);
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
            logger.LogInformation($"Pre-api low price handler called for {e.Auction.Uuid} profit {profit} users {localUsers.Count}");
        await Task.Delay(20_000).ConfigureAwait(false);
        // check if flip was sent to anyone 
        await Task.Delay(15_000).ConfigureAwait(false);
        // if not send to all users
    }
}