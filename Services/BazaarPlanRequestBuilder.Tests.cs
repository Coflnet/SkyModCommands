using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarPlanRequestBuilderTests
{
    private static readonly DateTime Uploaded = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static BazaarTraderState State(
        string[] strategies = null, double? horizon = 24, long cap = 0, long purse = 9_000_000, int freeSlots = 5,
        List<BazaarOrderInfo> open = null, List<SentBazaarOrderInfo> sent = null, string[] inventory = null,
        string[] goalTags = null, DateTime? updatedAt = null, bool withUpload = true)
        => new("uuid-1", AccountTier.PREMIUM, false, purse, freeSlots, strategies ?? ["Active"], horizon, cap,
            open ?? new(), sent ?? new(), inventory ?? [], goalTags ?? [], withUpload ? updatedAt ?? Uploaded : null, "rev-7");

    private static BazaarOrderInfo Order(string tag, BazaarOrderSide side, long amount, double price, long filled = 0)
        => new() { ItemTag = tag, ItemName = tag, Side = side, Amount = amount, PricePerUnit = price, FilledAmount = filled };

    private static SentBazaarOrderInfo Sent(string tag, BazaarOrderSide side, long amount, double price, bool confirmed = false)
        => new() { ItemTag = tag, ItemName = tag, Side = side, Amount = amount, PricePerUnit = price, ConfirmedAt = confirmed ? Uploaded : null };

    [Test]
    public void TraderCarriesModesFromSettings()
    {
        var trader = BazaarPlanRequestBuilder.Build(State(["Active", "Investing"], horizon: 12));

        Assert.That(trader.Id, Is.EqualTo("uuid-1"));
        Assert.That(trader.Strategies, Is.EqualTo(new[] { "Active", "Investing" }));
        Assert.That(trader.InvestingHorizonHours, Is.EqualTo(12));
        Assert.That(trader.ActiveHorizonHours, Is.EqualTo(1));
        Assert.That(trader.Priority, Is.EqualTo((int)AccountTier.PREMIUM * 2 + 1));
        Assert.That(trader.MaxItems, Is.EqualTo(5));
        Assert.That(trader.AccountState.ReserveOwner, Is.EqualTo("Caller"));
        Assert.That(trader.AccountState.FreeOrderSlots, Is.EqualTo(5));
        Assert.That(trader.AccountState.Revision, Is.EqualTo(Uploaded.ToString("O")));
        Assert.That(BazaarPlanRequestBuilder.Build(State(freeSlots: 30)).MaxItems, Is.EqualTo(10));
        Assert.That(BazaarPlanRequestBuilder.Build(State(freeSlots: 0)), Is.Null);
    }

    [Test]
    public void InvestingHorizonOnlySentWhenInvestingSelected()
    {
        Assert.That(BazaarPlanRequestBuilder.Build(State(["Active"], horizon: 24)).InvestingHorizonHours, Is.Null);
        Assert.That(BazaarPlanRequestBuilder.Build(State(["Investing"], horizon: 24)).InvestingHorizonHours, Is.EqualTo(24));
    }

    [Test]
    public void BudgetSubtractsOnlyUnplacedIntentsFromPurse()
    {
        var open = new List<BazaarOrderInfo>
        {
            Order("A", BazaarOrderSide.Buy, 100, 1000, filled: 40), // 60 remaining -> 60_000
            Order("B", BazaarOrderSide.Sell, 100, 5000),            // sells commit no coins
        };
        var sent = new List<SentBazaarOrderInfo>
        {
            Sent("C", BazaarOrderSide.Buy, 10, 20_000),             // unconfirmed -> 200_000
            Sent("D", BazaarOrderSide.Buy, 10, 50_000, confirmed: true), // confirmed: already in the open orders, never twice
            Sent("A", BazaarOrderSide.Buy, 100, 900),               // unconfirmed repriced twin of the open A order
        };

        var trader = BazaarPlanRequestBuilder.Build(State(purse: 9_000_000, open: open, sent: sent));

        // open commitment (60k) is already out of the purse: it only shrinks the 2/3 cap of (purse + commitment)
        Assert.That(trader.BudgetCoins, Is.EqualTo(2.0 / 3.0 * (9_000_000 + 60_000) - 60_000 - 200_000).Within(0.001));
    }

    [Test]
    public void BudgetCapBelowTwoThirdsPurseWins()
    {
        Assert.That(BazaarPlanRequestBuilder.Build(State(cap: 1_000_000, purse: 9_000_000)).BudgetCoins, Is.EqualTo(1_000_000));
        // cap above the purse is limited by the purse
        Assert.That(BazaarPlanRequestBuilder.Build(State(cap: 50_000_000, purse: 9_000_000)).BudgetCoins, Is.EqualTo(9_000_000));
        // fully committed -> no trader at all
        var open = new List<BazaarOrderInfo> { Order("A", BazaarOrderSide.Buy, 1000, 2000) };
        Assert.That(BazaarPlanRequestBuilder.Build(State(cap: 1_000_000, open: open)), Is.Null);
    }

    [Test]
    public void HeldTagsIncludeSellEscrowFilledUnclaimedInventoryAndPlanGoals()
    {
        var open = new List<BazaarOrderInfo>
        {
            Order("SELLING", BazaarOrderSide.Sell, 50, 100, filled: 10),
            Order("FILLED_BUY", BazaarOrderSide.Buy, 100, 10, filled: 30),
            Order("UNFILLED_BUY", BazaarOrderSide.Buy, 100, 10),
        };

        var held = BazaarPlanRequestBuilder.Build(State(open: open, inventory: ["IN_INV"], goalTags: ["IN_GOAL"])).AccountState.HeldExposure;

        Assert.That(held.Select(h => h.ItemTag), Is.EquivalentTo(new[] { "SELLING", "FILLED_BUY", "IN_INV", "IN_GOAL" }));
        var selling = held.Single(h => h.ItemTag == "SELLING");
        Assert.That(selling.Units, Is.EqualTo(40));
        Assert.That(selling.CostCoins, Is.EqualTo(4000));
        Assert.That(held.Single(h => h.ItemTag == "FILLED_BUY").CostCoins, Is.EqualTo(300));
        Assert.That(held.Single(h => h.ItemTag == "IN_INV").CostCoins, Is.EqualTo(0));
    }

    [Test]
    public void OpenBuyTagsIncludeSentUnconfirmedAtAnyPrice()
    {
        var open = new List<BazaarOrderInfo> { Order("OPEN", BazaarOrderSide.Buy, 10, 10), Order("SOLD", BazaarOrderSide.Sell, 10, 10) };
        var sent = new List<SentBazaarOrderInfo>
        {
            Sent("PENDING", BazaarOrderSide.Buy, 10, 777),
            Sent("CONFIRMED", BazaarOrderSide.Buy, 10, 10, confirmed: true),
            Sent("SENT_SELL", BazaarOrderSide.Sell, 10, 10),
        };

        var state = BazaarPlanRequestBuilder.Build(State(open: open, sent: sent)).AccountState;

        Assert.That(state.OpenBuyItemTags, Is.EquivalentTo(new[] { "OPEN", "PENDING", "CONFIRMED" }));
        Assert.That(state.UnresolvedIntentItemTags, Is.EqualTo(new[] { "PENDING" }));
    }

    [Test]
    public void TaxSourceAssumedWhenUnattested()
    {
        var trader = BazaarPlanRequestBuilder.Build(State());

        Assert.That(trader.SellTaxRate, Is.Null);
        Assert.That(trader.SellTaxSource, Is.EqualTo("Assumption"));
    }

    [Test]
    public void NoLiveStateAtAll_OmitsAccountState()
    {
        var trader = BazaarPlanRequestBuilder.Build(State(withUpload: false));

        Assert.That(trader, Is.Not.Null);
        Assert.That(trader.AccountState, Is.Null);
    }

    [Test]
    public void AccountStateSentWithoutUpload_IncludesInventoryAndGoals()
    {
        var before = DateTime.UtcNow;
        var sent = new List<SentBazaarOrderInfo> { Sent("PENDING", BazaarOrderSide.Buy, 10, 100) };

        var state = BazaarPlanRequestBuilder.Build(State(withUpload: false, inventory: ["IN_INV"], goalTags: ["IN_GOAL"], sent: sent)).AccountState;

        Assert.That(state, Is.Not.Null);
        Assert.That(state.StateAsOf, Is.GreaterThanOrEqualTo(before).And.LessThanOrEqualTo(DateTime.UtcNow), "request freshness, not GUI cadence");
        Assert.That(state.Revision, Is.EqualTo(BazaarPlanRequestBuilder.NoUploadRevision));
        Assert.That(state.HeldExposure.Select(h => h.ItemTag), Is.EquivalentTo(new[] { "IN_INV", "IN_GOAL" }));
        Assert.That(state.UnresolvedIntentItemTags, Is.EqualTo(new[] { "PENDING" }));
    }

    [Test]
    public void StateAsOfIsRequestTimeEvenWithOldUpload()
    {
        var state = BazaarPlanRequestBuilder.Build(State(updatedAt: DateTime.UtcNow.AddHours(-2))).AccountState;

        Assert.That(DateTime.UtcNow - state.StateAsOf!.Value, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void BudgetDoesNotSubtractPlacedBuyOrdersTwice()
    {
        var open = new List<BazaarOrderInfo> { Order("A", BazaarOrderSide.Buy, 20_000, 1000) }; // 20M committed, already out of the purse

        // purse 10M, cap 50M: min(50M - 20M, 10M) = 10M
        Assert.That(BazaarPlanRequestBuilder.Build(State(cap: 50_000_000, purse: 10_000_000, open: open)).BudgetCoins, Is.EqualTo(10_000_000));
        // default cap is 2/3 of (10M + 20M) = 20M, minus the 20M commitment = nothing left
        Assert.That(BazaarPlanRequestBuilder.Build(State(purse: 10_000_000, open: open)), Is.Null);
        // no open orders: 2/3 of the 30M purse
        Assert.That(BazaarPlanRequestBuilder.Build(State(purse: 30_000_000)).BudgetCoins, Is.EqualTo(20_000_000).Within(0.001));
    }

    [Test]
    public void RequestCarriesFastModeAndPreviewFlag()
    {
        var request = BazaarPlanRequestBuilder.BuildRequest([BazaarPlanRequestBuilder.Build(State())], 0.5, "inst", preview: true);

        Assert.That(request.Mode, Is.EqualTo("Fast"));
        Assert.That(request.TargetHoldTimeHours, Is.EqualTo(1));
        Assert.That(request.CapacityShare, Is.EqualTo(0.5));
        Assert.That(request.Preview, Is.True);
        Assert.That(Guid.TryParse(request.RequestId, out _), Is.True);
    }
}
