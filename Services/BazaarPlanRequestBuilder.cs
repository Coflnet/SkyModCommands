using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>Everything the planner request needs to know about one connected trader.</summary>
public record BazaarTraderState(
    string Uuid,
    AccountTier Tier,
    bool IsBot,
    long Purse,
    int FreeSlots,
    string[] Strategies,
    double? InvestingHorizonHours,
    long BudgetCapCoins,
    IReadOnlyList<BazaarOrderInfo> OpenOrders,
    IReadOnlyList<SentBazaarOrderInfo> SentOrders,
    IReadOnlyList<string> InventoryBazaarTags,
    IReadOnlyList<string> ActivePlanGoalTags,
    DateTime? OrdersUpdatedAt,
    string OrdersRevision);

/// <summary>
/// Pure adapter from a trader's live session state to a <see cref="FleetTraderRequest"/>. The caller
/// (this builder) is the single owner of the open/pending-order subtraction from the budget: the engine
/// receives an already net budget and <c>ReserveOwner="Caller"</c>.
/// <para>
/// Freshness: <c>AccountState.StateAsOf</c> is the moment this request is built (the backend's current knowledge
/// of inventory, sent intents and goals), so the engine's 120 s staleness rule judges request freshness and not
/// how often the user opens the orders GUI. <c>Revision</c> is the orders upload time when one is known.
/// </para>
/// </summary>
internal static class BazaarPlanRequestBuilder
{
    public const double ActiveHorizonHours = 1;
    public const string NoUploadRevision = "no-upload";
    public const string TaxSourceAssumption = "Assumption";

    /// <summary>The trader request, or null when there is no free slot or no budget left to contend with.</summary>
    public static FleetTraderRequest Build(BazaarTraderState s)
    {
        if (s.FreeSlots <= 0)
            return null;

        var open = s.OpenOrders ?? Array.Empty<BazaarOrderInfo>();
        var sent = s.SentOrders ?? Array.Empty<SentBazaarOrderInfo>();
        var openBuys = open.Where(o => o != null && o.Side == BazaarOrderSide.Buy).ToList();

        // Hypixel removes the coins of a placed buy order from the purse right away, so the open commitment is
        // already missing from s.Purse. It only shrinks the cap (the cap is a total-exposure limit); it is never
        // subtracted from the purse a second time. The default cap is 2/3 of everything the user owns in coins.
        var openCommitment = openBuys.Sum(o => o.RemainingAmount * o.PricePerUnit);
        var cap = s.BudgetCapCoins > 0 ? s.BudgetCapCoins : 2.0 / 3.0 * (s.Purse + openCommitment);
        var budget = Math.Min(cap - openCommitment, s.Purse);
        // a sent-but-unconfirmed buy that already shows up as an open order (repriced) is counted via the open order;
        // intents not placed yet have not left the purse, so they are subtracted
        var openBuyTags = openBuys.Select(o => o.ItemTag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        budget -= sent.Where(o => o != null && o.Side == BazaarOrderSide.Buy && o.ConfirmedAt == null && !openBuyTags.Contains(o.ItemTag))
            .Sum(o => o.Amount * o.PricePerUnit);
        if (budget <= 0)
            return null;

        var strategies = s.Strategies is { Length: > 0 } ? s.Strategies : ["Active"];
        var trader = new FleetTraderRequest
        {
            Id = s.Uuid,
            Priority = (int)s.Tier * 2 + (s.IsBot ? 0 : 1),
            BudgetCoins = budget,
            MaxItems = Math.Min(10, s.FreeSlots),
            IsBot = s.IsBot,
            Strategies = strategies.ToList(),
            InvestingHorizonHours = strategies.Contains("Investing") ? s.InvestingHorizonHours : null,
            ActiveHorizonHours = ActiveHorizonHours,
            SellTaxRate = null,
            SellTaxSource = TaxSourceAssumption,
        };
        if (HasLiveState(s, open, sent))
            trader.AccountState = BuildAccountState(s, open, sent);
        return trader;
    }

    private static bool HasLiveState(BazaarTraderState s, IReadOnlyList<BazaarOrderInfo> open, IReadOnlyList<SentBazaarOrderInfo> sent)
        => s.OrdersUpdatedAt != null || open.Any(o => o != null) || sent.Any(o => o != null)
            || (s.InventoryBazaarTags?.Count ?? 0) > 0 || (s.ActivePlanGoalTags?.Count ?? 0) > 0;

    private static FleetAccountStateDto BuildAccountState(BazaarTraderState s, IReadOnlyList<BazaarOrderInfo> open, IReadOnlyList<SentBazaarOrderInfo> sent)
    {
        var openBuyTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in open.Where(o => o != null && o.Side == BazaarOrderSide.Buy && !string.IsNullOrEmpty(o.ItemTag)))
            openBuyTags.Add(o.ItemTag);
        foreach (var o in sent.Where(o => o != null && o.Side == BazaarOrderSide.Buy && !string.IsNullOrEmpty(o.ItemTag)))
            openBuyTags.Add(o.ItemTag);
        var unresolved = sent.Where(o => o != null && o.Side == BazaarOrderSide.Buy && o.ConfirmedAt == null && !string.IsNullOrEmpty(o.ItemTag))
            .Select(o => o.ItemTag).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var held = new Dictionary<string, HeldExposureDto>(StringComparer.OrdinalIgnoreCase);
        void Hold(string tag, double units, double cost)
        {
            if (string.IsNullOrEmpty(tag))
                return;
            if (!held.TryGetValue(tag, out var entry))
                held[tag] = entry = new HeldExposureDto { ItemTag = tag };
            entry.Units += units;
            entry.CostCoins += cost;
        }
        foreach (var o in open.Where(o => o != null))
        {
            if (o.Side == BazaarOrderSide.Sell)
                Hold(o.ItemTag, o.RemainingAmount, o.RemainingAmount * o.PricePerUnit);
            else if (o.Side == BazaarOrderSide.Buy && o.FilledAmount > 0)
                Hold(o.ItemTag, o.FilledAmount, o.FilledAmount * o.PricePerUnit);
        }
        foreach (var tag in s.InventoryBazaarTags ?? Array.Empty<string>())
            Hold(tag, 0, 0);
        foreach (var tag in s.ActivePlanGoalTags ?? Array.Empty<string>())
            Hold(tag, 0, 0);

        return new FleetAccountStateDto
        {
            StateAsOf = DateTime.UtcNow,
            Revision = s.OrdersUpdatedAt?.ToString("O") ?? NoUploadRevision,
            OpenBuyItemTags = openBuyTags.ToList(),
            UnresolvedIntentItemTags = unresolved,
            HeldExposure = held.Values.ToList(),
            FreeOrderSlots = s.FreeSlots,
            ReserveOwner = "Caller",
        };
    }

    /// <summary>Request wrapper for a roster of traders (Fast mode, one hour hold, instance capacity share).</summary>
    public static FleetAllocateRequest BuildRequest(List<FleetTraderRequest> traders, double capacityShare, string instanceId, bool preview, bool includeUnqualifiedInvesting = false)
    {
        return new FleetAllocateRequest
        {
            Mode = "Fast",
            CapacityShare = capacityShare,
            TargetHoldTimeHours = 1,
            MaxCoinExposurePerItem = 0,
            Traders = traders,
            InstanceId = instanceId,
            RequestId = Guid.NewGuid().ToString("N"),
            Preview = preview,
            IncludeUnqualifiedInvesting = includeUnqualifiedInvesting,
        };
    }

    /// <summary>Tags of unfinished plan-backed bazaar flip goals; these items are already being worked on.</summary>
    public static List<string> ActivePlanGoalTags(Coflnet.Sky.Commands.Shared.Goals.GoalList goals)
    {
        return (goals?.Goals ?? new())
            .Where(g => g?.BazaarFlip != null && Coflnet.Sky.Commands.Shared.Goals.BazaarFlipProgress.Next(g) != Coflnet.Sky.Commands.Shared.Goals.BazaarFlipAction.Done)
            .Select(g => g.RootItemTag).ToList();
    }
}
