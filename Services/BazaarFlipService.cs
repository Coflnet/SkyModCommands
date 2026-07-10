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
using Coflnet.Sky.Items.Client.Model;
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
    private readonly FilterStateService filterStateService;
    private readonly IItemsApi itemsApi;
    private readonly ILogger<BazaarFlipService> logger;

    private static readonly Prometheus.Counter bazaarFlipsSent =
        Prometheus.Metrics.CreateCounter("sky_bazaar_flips_sent", "Count of bazaar flips distributed to users");

    private const int FlipsPerTierSlice = 3;
    private static readonly TimeSpan PremiumPlusFullListFallbackThreshold = TimeSpan.FromMinutes(5);

    public BazaarFlipService(
        FlipperService flipperService,
        IBazaarFlipperApi bazaarFlipperApi,
        IOrderBookApi orderBookApi,
        FilterStateService filterStateService,
        IItemsApi itemsApi,
        ILogger<BazaarFlipService> logger)
    {
        this.flipperService = flipperService;
        this.bazaarFlipperApi = bazaarFlipperApi;
        this.orderBookApi = orderBookApi;
        this.filterStateService = filterStateService;
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
        var ranked = await FetchRankedFlips();
        if (ranked.Count == 0)
            return;

        var names = await GetItemNames();
        var pools = TierCandidatePools.Build(ranked);

        foreach (var con in flipperService.Connections)
        {
            if (ct.IsCancellationRequested)
                break;
            await TryDistributeToConnection(con, pools, names);
        }
    }

    private async Task TryDistributeToConnection(
        FlipConWrapper con,
        TierCandidatePools pools,
        Dictionary<string, string> names)
    {
        try
        {
            if (con.Connection is not MinecraftSocket socket || socket.HasFlippingDisabled())
                return;
            if (!(socket.Settings?.AllowedFinders.HasFlag(LowPricedAuction.FinderType.Bazaar) ?? false))
                return;

            var useFallback = ShouldUseFullListFallback(socket.SessionInfo, DateTime.UtcNow);
            var candidates = pools.GetCandidatesFor(socket.SessionInfo, useFallback);
            if (candidates.Count == 0)
                return;

            await RecommendBestMatch(socket, names, candidates, useFallback);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error sending bazaar flip to user");
        }
    }

    /// <summary>
    /// Picks a bazaar order to recommend for this socket. In fallback mode the full ranked
    /// list is scanned in order until a sendable order is found; otherwise a single random
    /// pick from the tier slice is attempted.
    /// </summary>
    private async Task RecommendBestMatch(
        MinecraftSocket socket,
        Dictionary<string, string> names,
        IReadOnlyList<DemandFlip> candidates,
        bool useFallback)
    {
        if (await RejectIfAtOrderLimit(socket, names, candidates[0]))
            return;

        // In fallback mode the full ranked list is already ordered; otherwise try the user's own
        // tier bracket first (shuffled so users spread across the top items instead of all
        // competing for the single best one), then fall through to the next lower-tier candidates.
        var attempts = useFallback
            ? candidates
            : ShuffleBracketThenLowerTiers(candidates);

        foreach (var candidate in attempts)
        {
            if (await TrySendRecommendation(socket, names, candidate, recordBlockedReason: !useFallback))
                return;
        }
    }

    private static IEnumerable<DemandFlip> ShuffleBracketThenLowerTiers(IReadOnlyList<DemandFlip> candidates)
    {
        var bracket = candidates.Take(FlipsPerTierSlice).OrderBy(_ => Random.Shared.Next());
        return bracket.Concat(candidates.Skip(FlipsPerTierSlice));
    }

    private async Task<bool> RejectIfAtOrderLimit(
        MinecraftSocket socket,
        Dictionary<string, string> names,
        DemandFlip sampleCandidate)
    {
        if (!BazaarOrderStateHelper.HasReachedBuyOrderLimit(socket.SessionInfo.BazaarOrders))
            return false;

        var sample = await BuildVirtualFlipWithAmount(sampleCandidate, names);
        socket.sessionLifesycle.FlipProcessor.BlockedFlip(sample.Flip, "bazaar order limit");
        logger.LogDebug(
            "Skipping bazaar recommendation for {PlayerName} because {OrderCount} orders are already open",
            socket.SessionInfo.McName,
            socket.SessionInfo.ActiveBazaarOrderCount);
        return true;
    }

    private async Task<bool> TrySendRecommendation(
        MinecraftSocket socket,
        Dictionary<string, string> names,
        DemandFlip candidate,
        bool recordBlockedReason)
    {
        var prepared = await BuildVirtualFlipWithAmount(candidate, names);
        var flipProcessor = socket.sessionLifesycle.FlipProcessor;

        if (ExceedsPurseBudget(socket, candidate, prepared.Amount))
        {
            if (recordBlockedReason)
                flipProcessor.BlockedFlip(prepared.Flip, "purse check");
            return false;
        }

        if (!flipProcessor.FlipMatchesSetting(prepared.Flip, FlipperService.LowPriceToFlip(prepared.Flip)))
            return false;

        var price = await CalculateRecommendedBuyPrice(candidate);

        if (socket.ModAdapter is FullAfVersionAdapter fullAf
            && !fullAf.SendBazaarOrderRecommendation(candidate.ItemTag, prepared.Flip.Auction.ItemName, false, price, prepared.Amount, prepared.Category))
        {
            if (recordBlockedReason)
                flipProcessor.BlockedFlip(prepared.Flip, "bazaar order already sent");
            return false;
        }

        SendRecommendationDialog(socket, prepared.Flip, prepared.Amount, price);
        bazaarFlipsSent.Inc();
        return true;
    }

    private static bool ExceedsPurseBudget(MinecraftSocket socket, DemandFlip candidate, int amount)
    {
        return candidate.BuyPrice * amount > socket.sessionLifesycle.FlipProcessor.GetMaxCostFromPurse()
            && socket.SessionInfo.Purse > 0;
    }

    private async Task<double> CalculateRecommendedBuyPrice(DemandFlip candidate)
    {
        var orderBook = await orderBookApi.GetOrderBookAsync(candidate.ItemTag);
        var topBuy = orderBook.Buy.OrderByDescending(h => h.PricePerUnit).FirstOrDefault();
        return Math.Min(topBuy?.PricePerUnit ?? 0, candidate.BuyPrice) + 0.1;
    }

    private static void SendRecommendationDialog(MinecraftSocket socket, LowPricedAuction flip, int amount, double price)
    {
        socket.Dialog(db => db.MsgLine(
            $"Recommending an order of {McColorCodes.GREEN}{amount}x {McColorCodes.YELLOW}{flip.Auction.ItemName} " +
            $"{McColorCodes.GRAY}for {McColorCodes.GREEN}{socket.FormatPrice((long)price)}{McColorCodes.GRAY}",
            $"/bz {flip.Auction.ItemName}", "click to open on bazaar"));
    }

    private async Task<PreparedRecommendation> BuildVirtualFlipWithAmount(DemandFlip candidate, Dictionary<string, string> names)
    {
        var category = await BazaarOrderAmountHelper.GetKnownItemCategory(candidate.ItemTag, filterStateService);
        var amount = BazaarOrderAmountHelper.GetSuggestedBuyOrderAmount(candidate.ItemTag, candidate.SellPrice, category);
        var flip = CreateVirtualFlip(candidate, names, amount);
        return new PreparedRecommendation(flip, amount, category);
    }

    private async Task<List<DemandFlip>> FetchRankedFlips()
    {
        var copperTask = bazaarFlipperApi.CopperGetAsync();
        var demandFlips = await bazaarFlipperApi.DemandGetAsync();
        var copperItems = await copperTask;
        var mutations = copperItems.Select(m => m.ItemTag).ToHashSet();

        return demandFlips
            .Where(f => !mutations.Contains(f.ItemTag))
            .OrderByDescending(f => f.CurrentProfitPerHour)
            .ToList();
    }

    private async Task<Dictionary<string, string>> GetItemNames()
    {
        var itemNames = await itemsApi.ItemNamesGetAsync();
        return itemNames?.ToDictionary(i => i.Tag, i => i.Name) ?? [];
    }

    private static LowPricedAuction CreateVirtualFlip(DemandFlip candidate, Dictionary<string, string> names, int amount)
    {
        return new LowPricedAuction
        {
            DailyVolume = candidate.Volume,
            Finder = LowPricedAuction.FinderType.Bazaar,
            TargetPrice = (long)(candidate.SellPrice * amount),
            Auction = new SaveAuction
            {
                ItemName = BazaarUtils.GetSearchValue(candidate.ItemTag,
                    names.TryGetValue(candidate.ItemTag, out var dn) ? dn : candidate.ItemTag),
                Tag = candidate.ItemTag,
                Uuid = candidate.ItemTag,
                StartingBid = (long)(candidate.BuyPrice * amount),
                Enchantments = [],
                FlatenedNBT = []
            }
        };
    }

    /// <summary>
    /// Premium+ users who had no successful bazaar recommendation for a while fall back to
    /// scanning the full ranked list instead of staying confined to the top tier slice, so
    /// that users who filter many items still get a recommendation.
    /// </summary>
    internal static bool ShouldUseFullListFallback(SessionInfo sessionInfo, DateTime nowUtc)
    {
        if (sessionInfo == null || sessionInfo.SessionTier < AccountTier.PREMIUM_PLUS)
            return false;

        var lastRelevantRecommendation = sessionInfo.LastBazaarRecommendationAt ?? sessionInfo.ConnectedAt;
        return nowUtc - lastRelevantRecommendation >= PremiumPlusFullListFallbackThreshold;
    }

    /// <summary>
    /// Test-facing helper; returns the candidate pool a given session would use at a specific
    /// point in time by delegating to <see cref="TierCandidatePools"/>.
    /// </summary>
    internal static IReadOnlyList<DemandFlip> GetCandidatePool(
        List<DemandFlip> ranked,
        List<DemandFlip> premPlusFlips,
        List<DemandFlip> premiumFlips,
        List<DemandFlip> starterFlips,
        List<DemandFlip> freeFlips,
        SessionInfo sessionInfo,
        DateTime nowUtc)
    {
        var pools = new TierCandidatePools(ranked, premPlusFlips, premiumFlips, starterFlips, freeFlips);
        return pools.GetCandidatesFor(sessionInfo, ShouldUseFullListFallback(sessionInfo, nowUtc));
    }

    private readonly record struct PreparedRecommendation(LowPricedAuction Flip, int Amount, ItemCategory? Category);

    /// <summary>
    /// Pre-computed candidate pools for each tier plus the full ranked list used for the
    /// premium+ fallback scan.
    /// </summary>
    internal sealed class TierCandidatePools
    {
        private readonly IReadOnlyList<DemandFlip> ranked;
        private readonly IReadOnlyList<DemandFlip> premPlusSlice;
        private readonly IReadOnlyList<DemandFlip> premiumSlice;
        private readonly IReadOnlyList<DemandFlip> starterSlice;
        private readonly IReadOnlyList<DemandFlip> freeSlice;

        public TierCandidatePools(
            IReadOnlyList<DemandFlip> ranked,
            IReadOnlyList<DemandFlip> premPlusSlice,
            IReadOnlyList<DemandFlip> premiumSlice,
            IReadOnlyList<DemandFlip> starterSlice,
            IReadOnlyList<DemandFlip> freeSlice)
        {
            this.ranked = ranked;
            this.premPlusSlice = premPlusSlice;
            this.premiumSlice = premiumSlice;
            this.starterSlice = starterSlice;
            this.freeSlice = freeSlice;
        }

        public static TierCandidatePools Build(List<DemandFlip> ranked)
        {
            return new TierCandidatePools(
                ranked,
                TakeSlice(ranked, 0),
                TakeSlice(ranked, 1),
                TakeSlice(ranked, 2),
                TakeSlice(ranked, 3));
        }

        public IReadOnlyList<DemandFlip> GetCandidatesFor(SessionInfo sessionInfo, bool useFullListFallback)
        {
            if (useFullListFallback)
                return ranked;

            // Start with the user's own tier bracket, then include the next lower-tier slices so a
            // recommendation can still be found when every item in the bracket is already ordered.
            return (sessionInfo?.SessionTier ?? AccountTier.NONE) switch
            {
                >= AccountTier.PREMIUM_PLUS => Combine(premPlusSlice, premiumSlice, starterSlice),
                AccountTier.PREMIUM => Combine(premiumSlice, starterSlice, freeSlice),
                AccountTier.STARTER_PREMIUM => Combine(starterSlice, freeSlice),
                _ => freeSlice
            };
        }

        private static IReadOnlyList<DemandFlip> Combine(params IReadOnlyList<DemandFlip>[] slices)
        {
            return slices.SelectMany(slice => slice).ToList();
        }

        private static List<DemandFlip> TakeSlice(List<DemandFlip> ranked, int sliceIndex)
        {
            return ranked
                .Skip(sliceIndex * FlipsPerTierSlice)
                .Take(FlipsPerTierSlice)
                .ToList();
        }
    }
}
