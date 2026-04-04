using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Centrally fetches demand-based bazaar flips every 20 seconds,
/// constructs virtual <see cref="LowPricedAuction"/> objects for filter matching
/// and sends order recommendations to users via their mod adapter.
/// </summary>
public class BazaarFlipService : BackgroundService
{
    private readonly FlipperService flipperService;
    private readonly IBazaarFlipperApi bazaarFlipperApi;
    private readonly IOrderBookApi orderBookApi;
    private readonly IItemsApi itemsApi;
    private readonly ILogger<BazaarFlipService> logger;

    private static readonly Prometheus.Counter bazaarFlipsSent =
        Prometheus.Metrics.CreateCounter("sky_bazaar_flips_sent", "Count of bazaar flips distributed to users");

    private const int FlipsPerGroup = 3;

    public BazaarFlipService(
        FlipperService flipperService,
        IBazaarFlipperApi bazaarFlipperApi,
        IOrderBookApi orderBookApi,
        IItemsApi itemsApi,
        ILogger<BazaarFlipService> logger)
    {
        this.flipperService = flipperService;
        this.bazaarFlipperApi = bazaarFlipperApi;
        this.orderBookApi = orderBookApi;
        this.itemsApi = itemsApi;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        logger.LogInformation("BazaarFlipService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchAndDistribute(stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error fetching/distributing bazaar flips");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    private async Task FetchAndDistribute(CancellationToken ct)
    {
        var copperTask = bazaarFlipperApi.CopperGetAsync();
        var demandFlips = await bazaarFlipperApi.DemandGetAsync();
        var copperItems = await copperTask;
        var mutations = copperItems.Select(m => m.ItemTag).ToHashSet();

        var ranked = demandFlips
            .Where(f => !mutations.Contains(f.ItemTag))
            .OrderByDescending(f => f.CurrentProfitPerHour)
            .ToList();

        if (ranked.Count == 0)
            return;

        var names = await GetItemNames();

        // Build tier groups: prem+ gets top 3, premium next 3, starter next 3, free the rest
        var premPlusFlips = TakeGroup(ranked, 0);
        var premiumFlips = TakeGroup(ranked, 1);
        var starterFlips = TakeGroup(ranked, 2);
        var freeFlips = TakeGroup(ranked, 3);

        foreach (var con in flipperService.Connections)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (con.Connection is not MinecraftSocket socket || socket.HasFlippingDisabled())
                    continue;

                if (!socket.Settings?.AllowedFinders.HasFlag(LowPricedAuction.FinderType.Bazaar) ?? true)
                    continue;

                var tier = socket.SessionInfo.SessionTier;
                var group = tier switch
                {
                    >= AccountTier.PREMIUM_PLUS => premPlusFlips,
                    AccountTier.PREMIUM => premiumFlips,
                    AccountTier.STARTER_PREMIUM => starterFlips,
                    _ => freeFlips
                };

                if (group.Count == 0)
                    continue;

                await SendToSocket(socket, names, group);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error sending bazaar flip to user");
            }
        }
    }

    private async Task SendToSocket(
        MinecraftSocket socket,
        Dictionary<string, string> names,
        List<DemandFlip> group)
    {
        // pick one at random from the tier group
        var recommended = group[Random.Shared.Next(group.Count)];

        var isNotStackable = recommended.ItemTag.Contains("BOOK");
        var amount = recommended.SellPrice < 100_000 && !isNotStackable ? 64
            : recommended.SellPrice > 5_000_000 ? 1
            : 4;

        // build a virtual flip for filter matching only
        var virtualFlip = CreateVirtualFlip(recommended, names, amount);
        var flipInstance = FlipperService.LowPriceToFlip(virtualFlip);

        if (recommended.BuyPrice * amount > socket.sessionLifesycle.FlipProcessor.GetMaxCostFromPurse()
            && socket.SessionInfo.Purse > 0)
            return;

        if (!socket.sessionLifesycle.FlipProcessor.FlipMatchesSetting(virtualFlip, flipInstance))
            return;

        // get order book for optimal pricing
        var orderBook = await orderBookApi.GetOrderBookAsync(recommended.ItemTag);
        var topBuy = orderBook.Buy.OrderByDescending(h => h.PricePerUnit).FirstOrDefault();
        var price = Math.Min(topBuy?.PricePerUnit ?? 0, recommended.BuyPrice) + 0.1;

        if (socket.ModAdapter is FullAfVersionAdapter fullAf)
        {
            fullAf.SendBazaarOrderRecommendation(recommended.ItemTag, virtualFlip.Auction.ItemName, false, price, amount);
        }

        socket.Dialog(db => db.MsgLine(
            $"Recommending an order of {McColorCodes.GREEN}{amount}x {McColorCodes.YELLOW}{virtualFlip.Auction.ItemName} " +
            $"{McColorCodes.GRAY}for {McColorCodes.GREEN}{socket.FormatPrice((long)price)}{McColorCodes.GRAY}",
            $"/bz {virtualFlip.Auction.ItemName}", "click to open on bazaar"));

        bazaarFlipsSent.Inc();
    }

    private static List<DemandFlip> TakeGroup(List<DemandFlip> ranked, int groupIndex)
    {
        return ranked
            .Skip(groupIndex * FlipsPerGroup)
            .Take(FlipsPerGroup)
            .ToList();
    }

    private static LowPricedAuction CreateVirtualFlip(DemandFlip recommended, Dictionary<string, string> names, int amount)
    {
        return new LowPricedAuction
        {
            DailyVolume = recommended.Volume,
            Finder = LowPricedAuction.FinderType.Bazaar,
            TargetPrice = (long)(recommended.SellPrice * amount),
            Auction = new SaveAuction
            {
                ItemName = BazaarUtils.GetSearchValue(recommended.ItemTag,
                    names.TryGetValue(recommended.ItemTag, out var dn) ? dn : recommended.ItemTag),
                Tag = recommended.ItemTag,
                Uuid = recommended.ItemTag,
                StartingBid = (long)(recommended.BuyPrice * amount),
                Enchantments = [],
                FlatenedNBT = []
            }
        };
    }

    private async Task<Dictionary<string, string>> GetItemNames()
    {
        var itemNames = await itemsApi.ItemNamesGetAsync();
        return itemNames?.ToDictionary(i => i.Tag, i => i.Name)
            ?? [];
    }
}
