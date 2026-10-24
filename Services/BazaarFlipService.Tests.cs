using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarFlipServiceTests
{
    [Test]
    public void TopBuyHeldByOurUserWhenHighestBuyHasUserId()
    {
        var book = new OrderBook
        {
            Buy = new List<OrderEntry>
            {
                new() { PricePerUnit = 10, UserId = null },      // anonymous market depth
                new() { PricePerUnit = 12, UserId = "42" },      // our user holds the top slot
            }
        };

        Assert.That(BazaarFlipService.TopBuyHeldByOurUser(book), Is.True);
    }

    [Test]
    public void TopBuyNotHeldWhenHighestBuyIsAnonymous()
    {
        var book = new OrderBook
        {
            Buy = new List<OrderEntry>
            {
                new() { PricePerUnit = 12, UserId = null },      // market depth holds the top slot
                new() { PricePerUnit = 10, UserId = "42" },      // our user is only below the top
            }
        };

        Assert.That(BazaarFlipService.TopBuyHeldByOurUser(book), Is.False);
    }

    [Test]
    public void TopBuyNotHeldWhenBuySideEmpty()
    {
        Assert.That(BazaarFlipService.TopBuyHeldByOurUser(new OrderBook()), Is.False);
        Assert.That(BazaarFlipService.TopBuyHeldByOurUser(null), Is.False);
    }

    [Test]
    public void ShouldUseFullListFallbackForPremiumPlusAfterThreshold()
    {
        var now = DateTime.UtcNow;
        var session = new SessionInfo
        {
            SessionTier = AccountTier.PREMIUM_PLUS,
            ConnectedAt = now.AddMinutes(-6)
        };

        Assert.That(BazaarFlipService.ShouldUseFullListFallback(session, now), Is.True);
    }

    [Test]
    public void ShouldNotUseFullListFallbackWhenRecentRecommendationWasSent()
    {
        var now = DateTime.UtcNow;
        var session = new SessionInfo
        {
            SessionTier = AccountTier.PREMIUM_PLUS,
            ConnectedAt = now.AddHours(-1),
            LastBazaarRecommendationAt = now.AddMinutes(-4)
        };

        Assert.That(BazaarFlipService.ShouldUseFullListFallback(session, now), Is.False);
    }

    [TestCase(AccountTier.PREMIUM_PLUS, 0)]
    [TestCase(AccountTier.PREMIUM, 3)]
    [TestCase(AccountTier.STARTER_PREMIUM, 6)]
    [TestCase(AccountTier.NONE, 9)]
    public void GetCandidatePoolUsesTierBracketPlusLowerTiersWhenFallbackIsInactive(AccountTier tier, int expectedStart)
    {
        var now = DateTime.UtcNow;
        var ranked = CreateRanked(12);
        var session = new SessionInfo
        {
            SessionTier = tier,
            ConnectedAt = now.AddMinutes(-2),
            LastBazaarRecommendationAt = now.AddMinutes(-1)
        };

        var result = BazaarFlipService.GetCandidatePool(
            ranked,
            ranked.Take(3).ToList(),
            ranked.Skip(3).Take(3).ToList(),
            ranked.Skip(6).Take(3).ToList(),
            ranked.Skip(9).Take(3).ToList(),
            session,
            now);

        // own tier bracket (3) plus the next 6 lower-tier candidates, capped by what is available
        Assert.That(result.Select(f => f.ItemTag).ToArray(), Is.EqualTo(ranked.Skip(expectedStart).Take(9).Select(f => f.ItemTag).ToArray()));
    }

    [Test]
    public void GetCandidatePoolReturnsFullRankingForPremiumPlusFallback()
    {
        var now = DateTime.UtcNow;
        var ranked = CreateRanked(12);
        var session = new SessionInfo
        {
            SessionTier = AccountTier.PREMIUM_PLUS,
            ConnectedAt = now.AddMinutes(-7)
        };

        var result = BazaarFlipService.GetCandidatePool(
            ranked,
            ranked.Take(3).ToList(),
            ranked.Skip(3).Take(3).ToList(),
            ranked.Skip(6).Take(3).ToList(),
            ranked.Skip(9).Take(3).ToList(),
            session,
            now);

        Assert.That(result.Select(f => f.ItemTag).ToArray(), Is.EqualTo(ranked.Select(f => f.ItemTag).ToArray()));
    }

    private static List<DemandFlip> CreateRanked(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new DemandFlip
            {
                ItemTag = $"ITEM_{index}",
                CurrentProfitPerHour = count - index,
                BuyPrice = 1000 + index,
                SellPrice = 1100 + index,
                Volume = 100 - index
            })
            .ToList();
    }

    [Test]
    public void SelectOrderToSendExcludesHeldItems()
    {
        var allocation = new TraderAllocationDto
        {
            TraderId = "uuid-1",
            Orders = new List<OrderRecommendationDto>
            {
                new() { ItemTag = "ENCHANTED_COAL", PricePerUnit = 10, Amount = 100 },
                new() { ItemTag = "WHEAT", PricePerUnit = 5, Amount = 200 }
            }
        };
        var held = new HashSet<string> { "ENCHANTED_COAL" };

        var result = BazaarFlipService.SelectOrderToSend(allocation, held);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ItemTag, Is.EqualTo("WHEAT"));
    }

    [Test]
    public void SelectOrderToSendPicksFirstOrderAsEndpointReturnsRichestFirst()
    {
        var allocation = new TraderAllocationDto
        {
            TraderId = "uuid-1",
            Orders = new List<OrderRecommendationDto>
            {
                new() { ItemTag = "RICHEST_ITEM", PricePerUnit = 50, Amount = 100 },
                new() { ItemTag = "SECOND_ITEM", PricePerUnit = 10, Amount = 100 }
            }
        };

        var result = BazaarFlipService.SelectOrderToSend(allocation, new HashSet<string>());

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ItemTag, Is.EqualTo("RICHEST_ITEM"));
    }

    [Test]
    public void SelectOrderToSendReturnsNullWhenAllOffersAreHeld()
    {
        var allocation = new TraderAllocationDto
        {
            TraderId = "uuid-1",
            Orders = new List<OrderRecommendationDto>
            {
                new() { ItemTag = "ENCHANTED_COAL", PricePerUnit = 10, Amount = 100 },
                new() { ItemTag = "WHEAT", PricePerUnit = 5, Amount = 200 }
            }
        };
        var held = new HashSet<string> { "ENCHANTED_COAL", "WHEAT" };

        var result = BazaarFlipService.SelectOrderToSend(allocation, held);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SelectOrderToSendReturnsNullWhenNoOrders()
    {
        var allocation = new TraderAllocationDto { TraderId = "uuid-1", Orders = new List<OrderRecommendationDto>() };

        Assert.That(BazaarFlipService.SelectOrderToSend(allocation, new HashSet<string>()), Is.Null);
        Assert.That(BazaarFlipService.SelectOrderToSend(null, new HashSet<string>()), Is.Null);
    }

    private static TraderAllocationDto Allocation(params OrderRecommendationDto[] orders)
        => new() { TraderId = "uuid-1", Orders = orders.ToList() };

    private static OrderRecommendationDto Plan(string tag, string mode = "Active", string confidence = "Qualified", double amount = 10)
        => new() { ItemTag = tag, Mode = mode, Amount = amount, OriginalQuantity = (int)amount, PricePerUnit = 10, Confidence = new FillConfidenceDto { Status = confidence } };

    [Test]
    public void SelectOrderToSendSkipsItemWithOpenBuyAtDifferentPrice()
    {
        var uploaded = new List<BazaarOrderInfo> { new() { ItemTag = "COAL", ItemName = "Coal", Side = BazaarOrderSide.Buy, Amount = 10, PricePerUnit = 99 } };
        var sent = new List<SentBazaarOrderInfo> { new() { ItemTag = "WHEAT", Side = BazaarOrderSide.Buy, PricePerUnit = 1234 } };

        var result = BazaarFlipService.SelectOrderToSend(Allocation(Plan("COAL"), Plan("WHEAT"), Plan("SEEDS")),
            new HashSet<string>(), sent, uploaded);

        Assert.That(result?.ItemTag, Is.EqualTo("SEEDS"));
    }

    [Test]
    public void SelectOrderToSendSkipsUnqualifiedInvestingOrder()
    {
        var allocation = Allocation(Plan("A", "Investing", "InsufficientEvidence"), Plan("B", "Investing", "SimulationOnly"),
            Plan("C", "Investing", "Qualified"), Plan("D", "Active", "NotApplicable"));

        Assert.That(BazaarFlipService.SelectOrderToSend(allocation, new HashSet<string>())?.ItemTag, Is.EqualTo("C"));
        Assert.That(BazaarFlipService.SelectOrderToSend(Allocation(Plan("A", "Investing", "InsufficientEvidence")), new HashSet<string>()), Is.Null);
    }

    [Test]
    public void SelectOrderToSendSkipsActivePlanGoalTag()
    {
        var result = BazaarFlipService.SelectOrderToSend(Allocation(Plan("TRACKED"), Plan("OTHER")), new HashSet<string>(),
            activePlanTags: new HashSet<string> { "TRACKED" });

        Assert.That(result?.ItemTag, Is.EqualTo("OTHER"));
    }

    [Test]
    public void InvestingOnlyTraderDoesNotFallBackToTierSlice()
    {
        Assert.That(BazaarFlipService.AllowsLegacyFallback(new[] { "Investing" }), Is.False);
    }

    [Test]
    public void ActiveTraderMayFallBackToTierSlice()
    {
        Assert.That(BazaarFlipService.AllowsLegacyFallback(new[] { "Active" }), Is.True);
        Assert.That(BazaarFlipService.AllowsLegacyFallback(new[] { "Active", "Investing" }), Is.True);
    }

    [Test]
    public void UnavailableAssignmentRecordsBlockedReason()
    {
        var unavailable = new TraderAllocationDto { InvestingStatus = "Unavailable", InvestingReasonCodes = new() { "InvestingCandidatesNotReady", "X" } };

        Assert.That(BazaarFlipService.UnavailableReason(unavailable), Is.EqualTo("bazaar plan unavailable: InvestingCandidatesNotReady, X"));
        Assert.That(BazaarFlipService.UnavailableReason(new TraderAllocationDto { InvestingStatus = "EmptyQualified" }), Is.Null);
    }

    [Test]
    public void LocallyUnexecutableQuantityIsNotResized()
    {
        // books are capped to 4 per order locally; an engine quantity of 10 must be skipped, not shrunk
        Assert.That(BazaarFlipService.IsExecutableAsIs("ENCHANTMENT_SHARPNESS_5", 10, null), Is.False);
        Assert.That(BazaarFlipService.IsExecutableAsIs("ENCHANTMENT_SHARPNESS_5", 4, null), Is.True);
        Assert.That(BazaarFlipService.IsExecutableAsIs("ENCHANTED_COAL", 640, null), Is.True);

        var result = BazaarFlipService.SelectOrderToSend(
            Allocation(Plan("ENCHANTMENT_SHARPNESS_5", amount: 10), Plan("ENCHANTED_COAL", amount: 640)), new HashSet<string>(),
            isExecutable: o => BazaarFlipService.IsExecutableAsIs(o.ItemTag, o.Amount, null));
        Assert.That(result?.ItemTag, Is.EqualTo("ENCHANTED_COAL"));
    }
}
