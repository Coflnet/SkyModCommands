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
}