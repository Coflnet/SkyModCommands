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
using System.Linq;
using System.Globalization;
using System.Net;
using Payments.Client.Api;

/// <summary>
/// Handles events before the api update
/// </summary>
public class PreApiService : BackgroundService
{
    ConnectionMultiplexer redis;
    ILogger<PreApiService> logger;
    static private ConcurrentDictionary<IFlipConnection, DateTime> localUsers = new();
    IProductsApi productsApi;
    private List<string> preApiUsers = new();
    private ConcurrentDictionary<string, DateTime> sold = new();
    public PreApiService(ConnectionMultiplexer redis, FlipperService flipperService, ILogger<PreApiService> logger, IProductsApi productsApi)
    {
        this.redis = redis;
        this.logger = logger;

        flipperService.PreApiLowPriceHandler += PreApiLowPriceHandler;
        this.productsApi = productsApi;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        redis.GetSubscriber().Subscribe("auction_sell", (channel, message) =>
        {
            try
            {
                var sell = MessagePack.MessagePackSerializer.Deserialize<Sell>(message);
                sold.TryAdd(sell.Uuid, DateTime.UtcNow);
                if (sell.Uuid == Dns.GetHostName() && DateTime.Now.Minute % 5 == 0)
                    logger.LogInformation("got mod sell redis heartbeat");
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "failed to deserialize sell");
            }
        });
        // here to trigger the creation of the service
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SendEndWarnings();
                await RefreshUsers();
                PublishSell(Dns.GetHostName());
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "failed to execute pre api service refresh");
            }

            await Task.Delay(45000, stoppingToken);
        }
    }

    private static void SendEndWarnings()
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
    }

    public bool IsSold(string uuid)
    {
        return sold.ContainsKey(uuid);
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

        var tilPurchasable = e.Auction.Start + TimeSpan.FromSeconds(20) - DateTime.UtcNow;
        if (tilPurchasable < TimeSpan.Zero)
            tilPurchasable = TimeSpan.Zero;
        foreach (var item in localUsers.Keys)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    e = await SendFlipCorrectly(e, tilPurchasable, item).ConfigureAwait(false);
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

        await Task.Delay(tilPurchasable).ConfigureAwait(false);
        // check if flip was sent to anyone 
        await Task.Delay(20_000).ConfigureAwait(false);
        // if not send to all users
    }

    public async Task<LowPricedAuction> SendFlipCorrectly(LowPricedAuction flip, TimeSpan tilPurchasable, IFlipConnection connection)
    {
        var userCount = preApiUsers.Count == 0 ? 1 : preApiUsers.Count;
        var index = connection is MinecraftSocket socket ? preApiUsers.IndexOf(socket.UserId) : Random.Shared.Next(userCount);
        if (index == -1)
            logger.LogError($"User {connection.UserId} is not in pre api list");
        var isMyRR = flip.Auction.UId % userCount == index;
        if (!isMyRR)
            await Task.Delay(tilPurchasable + TimeSpan.FromSeconds(Random.Shared.Next(4, 8))).ConfigureAwait(false);
        else if (flip.Auction.Context.ContainsKey("cname"))
        {
            // copy the auction so we can modify it without affecting the original
            var context = flip.Auction.Context;
            flip = new LowPricedAuction(flip);
            flip.Auction.Context = new Dictionary<string, string>(context);
            flip.Auction.Context["cname"] = flip.Auction.Context["cname"].Replace(McColorCodes.DARK_GRAY + ".", McColorCodes.RED + ".");
        }
        logger.LogInformation($"Sent flip to {connection.UserId} for {flip.Auction.Uuid} ");
        var sendSuccessful = await connection.SendFlip(flip).ConfigureAwait(false);
        if (!sendSuccessful)
        {
            logger.LogInformation($"Failed to send flip to {connection.UserId} for {flip.Auction.Uuid}");
            localUsers.TryRemove(connection, out _);
        }
        if (localUsers.TryGetValue(connection, out var end) || end < DateTime.Now)
        {
            localUsers.TryRemove(connection, out _);
            logger.LogInformation("Removed user from flip list");
        }

        return flip;
    }

    public void PurchaseMessage(IMinecraftSocket connection, string message)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"^You purchased (.*) for (.*) coins");
        var match = regex.Match(message);
        var itemName = match.Groups[1].Value;
        var priceString = match.Groups[2].Value;
        var price = double.Parse(priceString, NumberStyles.Number, CultureInfo.InvariantCulture);
        var flip = connection.LastSent.Reverse().FirstOrDefault(f => f.Auction.ItemName == itemName && f.Auction.StartingBid == price);
        if (flip != null)
        {
            var uuid = flip.Auction.Uuid;
            logger.LogInformation($"Found flip that was bought by {connection.SessionInfo.McUuid} {uuid} at {DateTime.Now}");
            PublishSell(uuid);
        }
        else
            logger.LogInformation($"Could not find flip that was bought by {connection.SessionInfo.McUuid} {itemName} {price}");
    }

    private void PublishSell(string uuid)
    {
        redis.GetSubscriber().Publish("auction_sell", MessagePack.MessagePackSerializer.Serialize(new Sell { Uuid = uuid }));
    }

    [MessagePack.MessagePackObject]
    public class Sell
    {
        [MessagePack.Key(0)]
        public string Uuid { get; set; }
    }
}
