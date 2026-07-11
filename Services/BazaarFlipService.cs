using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Client.Model;
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
    /// <summary>
    /// Minimum time between two refills for the same user. Guards against rapid duplicate order
    /// uploads and against an upload-triggered refill overlapping a background cycle.
    /// </summary>
    private static readonly TimeSpan RefillThrottle = TimeSpan.FromSeconds(15);
    /// <summary>
    /// Time given to SkyBazaar to ingest freshly placed orders before the buy phase reads the order
    /// book back to see which items one of our users already holds the top buy on.
    /// </summary>
    private static readonly TimeSpan BuyStateSettleDelay = TimeSpan.FromMilliseconds(800);
    /// <summary>
    /// How many users' inventories are drained (sell orders placed) in parallel. Sell placement waits
    /// ~4s between orders, so serial draining would blow past the 20s cycle once there are many users.
    /// </summary>
    private const int SellPhaseConcurrency = 8;

    /// <summary>
    /// Snapshot of the most recently fetched candidate pools and item names, reused by
    /// <see cref="RefillOrders"/> so an order overview upload can refill orders
    /// without waiting for the next background cycle or refetching the flip list.
    /// </summary>
    private volatile TierCandidatePools latestPools;
    private volatile Dictionary<string, string> latestNames;

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
        latestPools = pools;
        latestNames = names;

        var sockets = ConnectedMacroBotSockets();
        if (sockets.Count == 0)
            return;

        // Phase 1 - always sell first, for every user, concurrently. Purchases must never outpace
        // sales (unsold items pile up and crash the client); sell placement also waits ~4s between
        // orders, so draining users serially would blow far past the 20s cycle once there are many.
        // A user who placed a sell this cycle keeps the rest of its slots and is not offered a buy.
        var buyers = await RunSellPhase(sockets, ct);
        if (buyers.Count == 0)
            return;

        // Let SkyBazaar ingest the orders just placed (and any from recent uploads) before we read the
        // book back to see who already holds the top buy.
        await Task.Delay(BuyStateSettleDelay, ct);

        await RunBuyDistribution(buyers, names, ct);
    }

    private List<MinecraftSocket> ConnectedMacroBotSockets()
    {
        var result = new List<MinecraftSocket>();
        foreach (var con in flipperService.Connections)
        {
            if (con.Connection is MinecraftSocket socket && socket.ModAdapter is FullAfVersionAdapter)
                result.Add(socket);
        }
        return result;
    }

    /// <summary>
    /// Runs the sell-first pass for every connected user in parallel and returns the users that are
    /// still eligible for a buy this cycle (passed the flipping/finder gating, have a free slot and
    /// had nothing to sell).
    /// </summary>
    private async Task<List<MinecraftSocket>> RunSellPhase(List<MinecraftSocket> sockets, CancellationToken ct)
    {
        var buyers = new System.Collections.Concurrent.ConcurrentBag<MinecraftSocket>();
        await Parallel.ForEachAsync(
            sockets,
            new ParallelOptions { MaxDegreeOfParallelism = SellPhaseConcurrency, CancellationToken = ct },
            async (socket, token) =>
            {
                try
                {
                    if (await SellThenQualifyForBuy(socket))
                        buyers.Add(socket);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error in bazaar sell phase for user");
                }
            });
        return buyers.ToList();
    }

    /// <summary>
    /// Applies the shared gating, records the refill attempt, drains the inventory into free slots and
    /// reports whether the user should still be offered a buy order this cycle. Selling is always
    /// attempted first so purchases can never outpace sales.
    /// </summary>
    private async Task<bool> SellThenQualifyForBuy(MinecraftSocket socket)
    {
        var now = DateTime.UtcNow;
        if (!PassesRefillGating(socket, now))
            return false;
        socket.SessionInfo.LastBazaarRefillAttempt = now;

        var freeSlots = BazaarOrderStateHelper.MaxTotalOrders - socket.SessionInfo.ActiveBazaarOrderCount;
        if (freeSlots <= 0)
            return false;

        if (socket.ModAdapter is FullAfVersionAdapter fullAf)
        {
            var sellsPlaced = await fullAf.PlaceInventorySellOrders(freeSlots);
            if (sellsPlaced > 0)
                return false; // sold something -> keep remaining slots, no buy this cycle
        }
        return true;
    }

    /// <summary>
    /// Distributes at most one buy order to each eligible user, best effort. Users with the most
    /// premium+ time left are served first; each order goes to a distinct item and never to an item
    /// whose top buy order is already held by one of our users (which would outbid ourselves). The
    /// order books for all candidates are fetched from SkyBazaar in a single bulk request.
    /// </summary>
    private async Task RunBuyDistribution(List<MinecraftSocket> buyers, Dictionary<string, string> names, CancellationToken ct)
    {
        var pools = latestPools;
        if (pools == null)
            return;

        var prepared = new List<(MinecraftSocket Socket, bool UseFallback, IReadOnlyList<DemandFlip> Candidates)>();
        foreach (var socket in buyers)
        {
            var useFallback = ShouldUseFullListFallback(socket.SessionInfo, DateTime.UtcNow);
            var candidates = pools.GetCandidatesFor(socket.SessionInfo, useFallback);
            if (candidates.Count > 0)
                prepared.Add((socket, useFallback, candidates));
        }
        if (prepared.Count == 0)
            return;

        var books = await FetchOrderBooks(prepared.SelectMany(p => p.Candidates.Select(c => c.ItemTag)));

        // longest premium+ time left first, so the most valuable subscriptions get first pick of a slot
        var assignedTags = new HashSet<string>();
        foreach (var buyer in prepared.OrderByDescending(p => p.Socket.sessionLifesycle.TierManager.ExpiresAt))
        {
            if (ct.IsCancellationRequested)
                break;
            await RecommendBestMatch(buyer.Socket, names, buyer.Candidates, buyer.UseFallback,
                BazaarOrderStateHelper.MaxOpenBuyOrders, books, assignedTags);
        }
    }

    private async Task<IReadOnlyDictionary<string, OrderBook>> FetchOrderBooks(IEnumerable<string> tags)
    {
        var distinct = tags.Distinct().ToList();
        if (distinct.Count == 0)
            return new Dictionary<string, OrderBook>();
        try
        {
            return await orderBookApi.GetOrderBooksAsync(distinct) ?? new Dictionary<string, OrderBook>();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error bulk fetching bazaar order books for {Count} candidates", distinct.Count);
            return new Dictionary<string, OrderBook>();
        }
    }

    private static bool PassesRefillGating(MinecraftSocket socket, DateTime now)
    {
        if (socket.HasFlippingDisabled())
            return false;
        if (!(socket.Settings?.AllowedFinders.HasFlag(LowPricedAuction.FinderType.Bazaar) ?? false))
            return false;
        if (now - socket.SessionInfo.LastBazaarRefillAttempt < RefillThrottle)
            return false; // refilled very recently (duplicate upload or overlapping background cycle)
        return true;
    }

    /// <summary>
    /// Tops up a single user's open bazaar orders. Selling is always attempted first so purchases
    /// can never outpace sales (unsold items pile up in the inventory and crash the client); only
    /// when there is nothing to sell is a single buy order placed, and only while the total open
    /// order count stays below <paramref name="buyOrderSlotCap"/>. Honors the user's flipping and
    /// finder settings; a declined buy is recorded as a bazaar blocked flip so
    /// <c>/cofl blocked bazaar</c> explains why nothing was placed.
    /// </summary>
    /// <param name="socket">The user connection whose open orders should be topped up.</param>
    /// <param name="buyOrderSlotCap">
    /// Highest total open order count at which a new buy order may still be placed. The background
    /// cycle fills up to <see cref="BazaarOrderStateHelper.MaxOpenBuyOrders"/>; order-overview
    /// uploads stop earlier to keep slots free for higher-priority "fast track" buys.
    /// </param>
    public async Task RefillOrders(MinecraftSocket socket, int buyOrderSlotCap)
    {
        try
        {
            // Prefer selling: drain the inventory into free slots before buying anything. Only when
            // there is nothing to sell is a single buy order attempted, which enforces the slot cap
            // and records a blocked reason when it declines, keeping room for fast-track buys.
            if (await SellThenQualifyForBuy(socket))
                await TryRecommendBuy(socket, buyOrderSlotCap, prefetchedBooks: null, assignedTags: null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error refilling bazaar orders for user");
        }
    }

    private async Task TryRecommendBuy(
        MinecraftSocket socket,
        int buyOrderSlotCap,
        IReadOnlyDictionary<string, OrderBook> prefetchedBooks,
        HashSet<string> assignedTags)
    {
        var pools = latestPools;
        var names = latestNames;
        if (pools == null || names == null)
            return;

        var useFallback = ShouldUseFullListFallback(socket.SessionInfo, DateTime.UtcNow);
        var candidates = pools.GetCandidatesFor(socket.SessionInfo, useFallback);
        if (candidates.Count == 0)
            return;

        await RecommendBestMatch(socket, names, candidates, useFallback, buyOrderSlotCap, prefetchedBooks, assignedTags);
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
        bool useFallback,
        int buyOrderSlotCap,
        IReadOnlyDictionary<string, OrderBook> prefetchedBooks,
        HashSet<string> assignedTags)
    {
        if (await RejectIfAtOrderLimit(socket, names, candidates[0], buyOrderSlotCap))
            return;

        // In fallback mode the full ranked list is already ordered; otherwise try the user's own
        // tier bracket first (shuffled so users spread across the top items instead of all
        // competing for the single best one), then fall through to the next lower-tier candidates.
        var attempts = useFallback
            ? candidates
            : ShuffleBracketThenLowerTiers(candidates);

        foreach (var candidate in attempts)
        {
            // In a background cycle each item is handed out to at most one user, so several users
            // don't pile onto (and outbid each other for) the same top item.
            if (assignedTags != null && assignedTags.Contains(candidate.ItemTag))
                continue;
            if (await TrySendRecommendation(socket, names, candidate, recordBlockedReason: !useFallback, prefetchedBooks))
            {
                assignedTags?.Add(candidate.ItemTag);
                return;
            }
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
        DemandFlip sampleCandidate,
        int buyOrderSlotCap)
    {
        if (socket.SessionInfo.ActiveBazaarOrderCount < buyOrderSlotCap)
            return false;

        var sample = await BuildVirtualFlipWithAmount(sampleCandidate, names);
        socket.sessionLifesycle.FlipProcessor.BlockedFlip(sample.Flip, "bazaar order limit");
        logger.LogDebug(
            "Skipping bazaar recommendation for {PlayerName} because {OrderCount} orders are already open (cap {Cap})",
            socket.SessionInfo.McName,
            socket.SessionInfo.ActiveBazaarOrderCount,
            buyOrderSlotCap);
        return true;
    }

    private async Task<bool> TrySendRecommendation(
        MinecraftSocket socket,
        Dictionary<string, string> names,
        DemandFlip candidate,
        bool recordBlockedReason,
        IReadOnlyDictionary<string, OrderBook> prefetchedBooks)
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

        var orderBook = await GetOrderBook(candidate.ItemTag, prefetchedBooks);
        var topBuy = TopBuyOrder(orderBook);

        // Never outbid an order already held by one of our users; placing above it would only push
        // our own user down.
        if (TopBuyHeldByOurUser(orderBook))
        {
            if (recordBlockedReason)
                flipProcessor.BlockedFlip(prepared.Flip, "bazaar top order held by our user");
            return false;
        }

        var price = Math.Min(topBuy?.PricePerUnit ?? 0, candidate.BuyPrice) + 0.1;

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

    private async Task<OrderBook> GetOrderBook(string itemTag, IReadOnlyDictionary<string, OrderBook> prefetchedBooks)
    {
        if (prefetchedBooks != null && prefetchedBooks.TryGetValue(itemTag, out var book))
            return book;
        return await orderBookApi.GetOrderBookAsync(itemTag);
    }

    private static OrderEntry TopBuyOrder(OrderBook book)
    {
        return book?.Buy?.OrderByDescending(h => h.PricePerUnit).FirstOrDefault();
    }

    /// <summary>
    /// True when the highest buy order is held by one of our users. SkyBazaar imports raw Hypixel
    /// market depth with an empty UserId, so a non-empty top-buy UserId means one of our customers
    /// already holds that slot and we must not outbid it.
    /// </summary>
    internal static bool TopBuyHeldByOurUser(OrderBook book)
    {
        var topBuy = TopBuyOrder(book);
        return topBuy != null && !string.IsNullOrEmpty(topBuy.UserId);
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
