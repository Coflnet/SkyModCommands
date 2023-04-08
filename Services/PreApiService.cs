namespace Coflnet.Sky.ModCommands.Services;

using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using Coflnet.Sky.Core;
using Coflnet.Sky.Proxy.Client.Api;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Commands.Shared;
using System.Collections.Concurrent;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using System.Linq;
using System.Globalization;
using System.Net;
using Payments.Client.Api;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Handles events before the api update
/// </summary>
public class PreApiService : BackgroundService
{
    ConnectionMultiplexer redis;
    ILogger<PreApiService> logger;
    static private ConcurrentDictionary<IFlipConnection, DateTime> localUsers = new();
    IProductsApi productsApi;
    IBaseApi baseApi;
    Prometheus.Counter flipsPurchased = Prometheus.Metrics.CreateCounter("sky_mod_flips_purchased", "Flips purchased");
    Prometheus.Counter preApiFlipPurchased = Prometheus.Metrics.CreateCounter("sky_mod_flips_purchased_preapi", "Flips bought by a preapi user");
    private List<string> preApiUsers = new();
    private ConcurrentDictionary<string, AccountTier> sold = new();
    private ConcurrentDictionary<string, DateTime> sent = new();
    private ConcurrentDictionary<int, List<IMinecraftSocket>> notifyWhenUserLeave = new();
    public int PreApiUserCount => preApiUsers.Count;
    public PreApiService(ConnectionMultiplexer redis, FlipperService flipperService, ILogger<PreApiService> logger, IProductsApi productsApi, IBaseApi baseApi)
    {
        this.redis = redis;
        this.logger = logger;

        flipperService.PreApiLowPriceHandler += PreApiLowPriceHandler;
        this.productsApi = productsApi;
        this.baseApi = baseApi;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        redis.GetSubscriber().Subscribe("auction_sell", (channel, message) =>
        {
            try
            {
                var sell = MessagePack.MessagePackSerializer.Deserialize<Auction>(message);
                sold.TryAdd(sell.Uuid, sell.Tier);
                sent.TryRemove(sell.Uuid, out _);
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "failed to deserialize sell");
            }
        });
        redis.GetSubscriber().Subscribe("auction_sent", (channel, message) =>
        {
            try
            {
                var send = MessagePack.MessagePackSerializer.Deserialize<Auction>(message);
                sent.AddOrUpdate(send.Uuid, DateTime.UtcNow, (key, old) => DateTime.UtcNow);
                if (send.Uuid == Dns.GetHostName() && DateTime.UtcNow.Minute % 5 == 0)
                    logger.LogInformation("got mod sent redis heartbeat");
                else
                    logger.LogInformation($"got mod sent confirm from {send.Uuid}");
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "failed to deserialize send");
            }
        });
        // here to trigger the creation of the service
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SendEndWarnings();
                await RefreshUsers();
                PublishSell(Dns.GetHostName(), AccountTier.NONE);
                PublishReceive(Dns.GetHostName());
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "failed to execute pre api service refresh");
            }

            await Task.Delay(45000, stoppingToken).ConfigureAwait(false);
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

    public void AddNotify(int count, IMinecraftSocket socket)
    {
        notifyWhenUserLeave.AddOrUpdate(count, new List<IMinecraftSocket>() { socket }, (key, old) =>
        {
            old.Add(socket);
            return old;
        });
    }

    public bool IsSold(string uuid)
    {
        return sold.ContainsKey(uuid);
    }
    public AccountTier SoldToTier(string uuid)
    {
        return sold.GetValueOrDefault(uuid);
    }

    private async Task RefreshUsers()
    {
        try
        {
            preApiUsers = await productsApi.ProductsServiceServiceSlugIdsGetAsync("pre_api");
            if (notifyWhenUserLeave.TryGetValue(preApiUsers.Count, out var sockets))
            {
                foreach (var item in sockets)
                {
                    item.Dialog(db => db.CoflCommand<PurchaseCommand>($"There are now {McColorCodes.RED}{preApiUsers.Count}{McColorCodes.WHITE} users with {McColorCodes.RED}pre api{McColorCodes.WHITE}\nClick {McColorCodes.RED}here{McColorCodes.WHITE} to purchase", "pre_api",
                    $"{McColorCodes.RED}Starts the purchase for an hour of {McColorCodes.RED}pre api{McColorCodes.WHITE}"));
                }
                notifyWhenUserLeave.TryRemove(preApiUsers.Count, out _);
            }
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
        try
        {
            await DistributeFlip(e).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, $"Error while handling pre api low price {JSON.Stringify(localUsers)}\n{JSON.Stringify(e)}");
        }
    }

    private async Task<LowPricedAuction> DistributeFlip(LowPricedAuction e)
    {
        if (e.Auction?.Context?.ContainsKey("cname") ?? false)
            e.Auction.Context["cname"] += McColorCodes.DARK_GRAY + ".";

        var tilPurchasable = e.Auction.Start + TimeSpan.FromSeconds(19.9) - DateTime.UtcNow;
        if (tilPurchasable < TimeSpan.Zero)
            tilPurchasable = TimeSpan.Zero;
        foreach (var item in localUsers.Keys)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendFlipCorrectly(e, tilPurchasable, item).ConfigureAwait(false);
                }
                catch (System.Exception e)
                {
                    logger.LogError(e, "Error while sending flip to user");
                }
            }, new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token).ConfigureAwait(false);
        }
        var profit = e.TargetPrice - e.Auction?.StartingBid;
        if (profit > 1_000_000)
            logger.LogInformation($"Pre-api low price handler called for {e?.Auction?.Uuid} profit {profit} users {localUsers?.Count}");

        if (tilPurchasable >= TimeSpan.Zero)
            await Task.Delay(tilPurchasable).ConfigureAwait(false);
        else
            await Task.Delay(TimeSpan.FromSeconds(0.3)).ConfigureAwait(false);
        // check if flip was sent to anyone 
        if (!sent.ContainsKey(e.Auction.Uuid))
            return e; // if not send to all users

        // send out after delay
        await Task.Delay(4_000).ConfigureAwait(false);
        return e;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="flip"></param>
    /// <param name="tilPurchasable"></param>
    /// <param name="connection"></param>
    /// <returns>true if delay should be skipped for others</returns>
    public async Task SendFlipCorrectly(LowPricedAuction flip, TimeSpan tilPurchasable, IFlipConnection connection)
    {
        var profit = flip.TargetPrice - flip.Auction?.StartingBid;
        if (profit < 300_000 && flip.Finder != LowPricedAuction.FinderType.USER)
            return;
        var userCount = preApiUsers.Count == 0 ? 1 : preApiUsers.Count;
        var index = connection is MinecraftSocket socket ? preApiUsers.IndexOf(socket.UserId) : Random.Shared.Next(userCount);
        if (index == -1)
            logger.LogError($"User {connection.UserId} is not in pre api list");
        var isMyRR = Math.Abs(flip.Auction.UId) % userCount == index;
        if (!isMyRR)
        {
            logger.LogInformation($"Waiting {tilPurchasable} for {flip.Auction.Uuid} to send to {connection.UserId} active users {JSON.Stringify(preApiUsers)}");
            await WaitTwoSecondsBefore(tilPurchasable).ConfigureAwait(false);
            var extraWait = tilPurchasable > TimeSpan.Zero ? TimeSpan.FromSeconds(1.8) : TimeSpan.Zero;
            // check if rr was sent to user, if not send to all users
            if (sent.ContainsKey(flip.Auction.Uuid))
                await Task.Delay(TimeSpan.FromSeconds(3 + Random.Shared.NextDouble() * 1) + extraWait).ConfigureAwait(false);
            else
            {
                flip = ChangeFlipDotColor(flip, McColorCodes.GREEN);
            }
        }
        else if (flip.Auction.Context.ContainsKey("cname"))
        {
            flip = ChangeFlipDotColor(flip, McColorCodes.RED);
            flip.AdditionalProps["isRR"] = "y";
        }
        if (profit > 1_000_000)
            logger.LogInformation($"Is rr {isMyRR}, Sent flip to {connection.UserId} for {flip.Auction.Uuid} active users {JSON.Stringify(preApiUsers)} "
                                + $"index {index} {flip.Auction.UId % userCount} forward {!sent.ContainsKey(flip.Auction.Uuid)} {flip.Auction.Context.GetValueOrDefault("pre-api")}");

        flip.AdditionalProps["da"] = (DateTime.UtcNow - flip.Auction.FindTime).ToString();
        var sendSuccessful = await connection.SendFlip(flip).ConfigureAwait(false);
        if (!sendSuccessful)
        {
            logger.LogInformation($"Failed to send flip to {connection.UserId} for {flip.Auction.Uuid}");
            localUsers.TryRemove(connection, out _);
        }
        if (!localUsers.TryGetValue(connection, out var end) || end < DateTime.UtcNow)
        {
            localUsers.TryRemove(connection, out _);
            logger.LogInformation("Removed user from flip list");
        }

        if (tilPurchasable > TimeSpan.FromSeconds(2.5))
            await Task.Delay(tilPurchasable - TimeSpan.FromSeconds(2.5)).ConfigureAwait(false);

        if ((connection as MinecraftSocket)?.LastSent.Any(f => f.UId == flip.UId) ?? false)
        {
            logger.LogInformation($"Flip was sent out to {(connection as MinecraftSocket).SessionInfo.McName} {flip.Auction.Uuid}");
            PublishReceive(flip.Auction.Uuid);
        }
    }

    private static async Task WaitTwoSecondsBefore(TimeSpan tilPurchasable)
    {
        var toWait = tilPurchasable - TimeSpan.FromSeconds(2);
        if (toWait < TimeSpan.FromSeconds(1))
            toWait = TimeSpan.FromSeconds(1);
        await Task.Delay(toWait).ConfigureAwait(false);
    }

    private static LowPricedAuction ChangeFlipDotColor(LowPricedAuction flip, string color)
    {
        var context = flip.Auction.Context;
        if (context == null)
            return flip;
        flip = new LowPricedAuction(flip);
        flip.Auction.Context = new Dictionary<string, string>(context);
        flip.Auction.Context["cname"] = flip.Auction.Context["cname"].Replace(McColorCodes.DARK_GRAY + ".", color + ".");
        flip.AdditionalProps = new(flip.AdditionalProps);
        return flip;
    }

    public void PurchaseMessage(IMinecraftSocket connection, string message)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"^You purchased (.*) for (.*) coins");
        var match = regex.Match(message);
        var itemName = match.Groups[1].Value;
        var priceString = match.Groups[2].Value;
        var price = double.Parse(priceString, NumberStyles.Number, CultureInfo.InvariantCulture);
        var flips = connection.LastSent.OrderByDescending(s => s.Auction.Start)
            .Where(f => f.Auction.ItemName == itemName 
                    && f.Auction.StartingBid == price 
                    && f.Auction.Start > DateTime.UtcNow.AddMinutes(-2));
        if (flips.Count() == 1)
        {
            var flip = flips.FirstOrDefault();
            var uuid = flip.Auction.Uuid;
            logger.LogInformation($"Found flip that was bought by {connection.SessionInfo.McUuid} {uuid} at {DateTime.UtcNow}");
            PublishSell(uuid, connection.UserAccountTier().Result);
            CheckHighProfitpurchaser(connection, flip);
            flipsPurchased.Inc();
            if(connection.AccountInfo.Tier >= AccountTier.SUPER_PREMIUM)
                preApiFlipPurchased.Inc();
        }
        else
            logger.LogInformation($"Could not find flip that was bought by {connection.SessionInfo.McUuid} {itemName} {price}");
    }

    private void CheckHighProfitpurchaser(IMinecraftSocket connection, LowPricedAuction flip)
    {
        if (flip.TargetPrice - flip.Auction.StartingBid < 3_000_000)
            return;
        var uuid = flip.Auction.Uuid;
        connection.TryAsyncTimes(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            var auction = await AuctionService.Instance.GetAuctionAsync(uuid, db => db.Include(a => a.Bids));
            if (auction == null)
            {
                logger.LogInformation($"skipcheck not find auction {uuid} for high profit purchaser {connection.SessionInfo.McUuid}");
                return;
            }
            var buyer = auction.Bids.FirstOrDefault()?.Bidder;
            if (buyer == null)
            {
                logger.LogInformation($"skipcheck not find buyer for auction {uuid} for high profit purchaser {connection.SessionInfo.McUuid}");
                return;
            }
            if (buyer == connection.SessionInfo.McUuid)
                return;
            logger.LogInformation($"skipcheck Changing used uuid to {buyer} for {connection.SessionInfo.McName} from {connection.SessionInfo.McUuid}");
            var connectedFrom = connection.SessionInfo.McUuid;
            connection.SessionInfo.MinecraftUuids.Add(buyer);
            try
            {
                var sim = await connection.GetService<FlipTracker.Client.Api.IAnalyseApi>().PlayerPlayerIdAlternativeGetAsync(buyer, 1);
                var simPlayerId = long.Parse(sim.PlayerId);
                var connectedUid = AuctionService.Instance.GetId(connectedFrom);
                var buyerUid = AuctionService.Instance.GetId(buyer);
                logger.LogInformation($"skipcheck Found {sim.BoughtCount} {sim.TargetReceived} similar buys from {sim.PlayerId} for {buyerUid} {connectedUid} connected as {connection.SessionInfo.McName}");
                if (sim.BoughtCount > 20 && sim.BoughtCount - sim.TargetReceived < 5 && simPlayerId == connectedUid)
                {
                    logger.LogInformation($"skipcheck Adding Account {sim.PlayerId} for {connection.SessionInfo.McName} from {connectedFrom} by {buyer} for {flip.Auction.Uuid}");
                    connection.AccountInfo.McIds.Add(buyer);
                    connection.SessionInfo.McUuid = buyer;
                    connection.SessionInfo.VerifiedMc = false;
                    await connection.sessionLifesycle.AccountInfo.Update();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"skipcheck Error finding similar buys for {buyer} {connection.SessionInfo.McUuid}");
            }
        }, "skipcheck verify mc uuid", 1);
    }

    public async Task ListingMessage(IMinecraftSocket connection, string message)
    {
        await baseApi.BaseAhPlayerIdPostAsync(connection.SessionInfo.McUuid).ConfigureAwait(false);
        logger.LogInformation($"Checking auctions for {connection.SessionInfo.McName} {connection.SessionInfo.McUuid} {message}");
    }

    private void PublishSell(string uuid, AccountTier tier)
    {
        redis.GetSubscriber().Publish("auction_sell", MessagePack.MessagePackSerializer.Serialize(new Auction { Uuid = uuid, Tier = tier }));
    }

    public void PublishReceive(string uuid)
    {
        if (sold.ContainsKey(uuid))
            return;
        redis.GetSubscriber().Publish("auction_sent", MessagePack.MessagePackSerializer.Serialize(new Auction { Uuid = uuid }));
    }

    [MessagePack.MessagePackObject]
    public class Auction
    {
        [MessagePack.Key(0)]
        public string Uuid { get; set; }
        [MessagePack.Key(1)]
        public AccountTier Tier { get; set; }
    }
}
