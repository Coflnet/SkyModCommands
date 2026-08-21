using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
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
    public void BuildFleetTraderRequestReturnsNullWhenNoFreeSlots()
    {
        var result = BazaarFlipService.BuildFleetTraderRequest("uuid-1", AccountTier.PREMIUM, isBot: false, purse: 1_000_000, freeSlots: 0);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildFleetTraderRequestReturnsNullWhenNoBudget()
    {
        var result = BazaarFlipService.BuildFleetTraderRequest("uuid-1", AccountTier.PREMIUM, isBot: false, purse: 0, freeSlots: 5);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildFleetTraderRequestReturnsNullWhenPurseIsNegative()
    {
        // a purse of -1 signals "not flipable" (see SessionInfo.IsNotFlipable) - must never yield a budget
        var result = BazaarFlipService.BuildFleetTraderRequest("uuid-1", AccountTier.PREMIUM, isBot: false, purse: -1, freeSlots: 5);

        Assert.That(result, Is.Null);
    }

    [TestCase(AccountTier.NONE, false, 1)]
    [TestCase(AccountTier.NONE, true, 0)]
    [TestCase(AccountTier.STARTER_PREMIUM, false, 3)]
    [TestCase(AccountTier.PREMIUM, false, 5)]
    [TestCase(AccountTier.PREMIUM, true, 4)]
    [TestCase(AccountTier.PREMIUM_PLUS, false, 7)]
    [TestCase(AccountTier.SUPER_PREMIUM, false, 9)]
    [TestCase(AccountTier.SUPER_PREMIUM, true, 8)]
    public void BuildFleetTraderRequestEncodesPriorityAsTierDominantThenRealUserOverBot(AccountTier tier, bool isBot, int expectedPriority)
    {
        var result = BazaarFlipService.BuildFleetTraderRequest("uuid-1", tier, isBot, purse: 900_000, freeSlots: 5);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Priority, Is.EqualTo(expectedPriority));
    }

    [Test]
    public void BuildFleetTraderRequestUsesTwoThirdsOfPurseAsBudget()
    {
        var result = BazaarFlipService.BuildFleetTraderRequest("uuid-1", AccountTier.PREMIUM, isBot: false, purse: 900_000, freeSlots: 5);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.BudgetCoins, Is.EqualTo(600_000).Within(0.001));
    }

    [Test]
    public void BuildFleetTraderRequestCapsMaxItemsAtTen()
    {
        var result = BazaarFlipService.BuildFleetTraderRequest("uuid-1", AccountTier.PREMIUM, isBot: false, purse: 900_000, freeSlots: 21);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MaxItems, Is.EqualTo(10));
    }

    [Test]
    public void BuildFleetTraderRequestKeepsMaxItemsBelowTenWhenFreeSlotsAreFewer()
    {
        var result = BazaarFlipService.BuildFleetTraderRequest("uuid-1", AccountTier.PREMIUM, isBot: false, purse: 900_000, freeSlots: 4);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MaxItems, Is.EqualTo(4));
    }

    [Test]
    public void BuildFleetTraderRequestSetsIdAndIsBot()
    {
        var result = BazaarFlipService.BuildFleetTraderRequest("uuid-42", AccountTier.STARTER_PREMIUM, isBot: true, purse: 900_000, freeSlots: 5);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo("uuid-42"));
        Assert.That(result.IsBot, Is.True);
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
}