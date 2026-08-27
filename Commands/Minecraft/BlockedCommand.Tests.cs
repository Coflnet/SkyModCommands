using System;
using System.Linq;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class BlockedCommandTests
{
    [TestCase("bazaar", true)]
    [TestCase("bz", true)]
    [TestCase("hyperion", false)]
    public void IsBazaarSearchRecognizesBazaarAliases(string search, bool expected)
    {
        Assert.That(BlockedCommand.IsBazaarSearch(search), Is.EqualTo(expected));
    }

    [Test]
    public void MatchesSearchFiltersBazaarByFinder()
    {
        var bazaar = new MinecraftSocket.BlockedElement
        {
            Flip = new LowPricedAuction
            {
                Finder = LowPricedAuction.FinderType.Bazaar,
                Auction = new SaveAuction { ItemName = "Volcanic Rock", Tag = "VOLCANIC_ROCK", Uuid = "VOLCANIC_ROCK" }
            },
            Reason = "bazaar order limit"
        };
        var ah = new MinecraftSocket.BlockedElement
        {
            Flip = new LowPricedAuction
            {
                Finder = LowPricedAuction.FinderType.SNIPER,
                Auction = new SaveAuction { ItemName = "Hyperion", Tag = "HYPERION", Uuid = System.Guid.NewGuid().ToString() }
            },
            Reason = "minProfit"
        };

        Assert.That(BlockedCommand.MatchesSearch(bazaar, "bazaar"), Is.True);
        Assert.That(BlockedCommand.MatchesSearch(bazaar, "bz"), Is.True);
        Assert.That(BlockedCommand.MatchesSearch(ah, "bazaar"), Is.False);
    }

    [Test]
    public void BazaarBlockedEntriesUseBazaarNavigation()
    {
        var flip = new LowPricedAuction
        {
            Finder = LowPricedAuction.FinderType.Bazaar,
            Auction = new SaveAuction { ItemName = "Volcanic Rock", Tag = "VOLCANIC_ROCK", Uuid = "VOLCANIC_ROCK" }
        };

        Assert.That(BlockedCommand.GetDetailsLink(flip), Is.EqualTo("https://sky.coflnet.com/item/VOLCANIC_ROCK"));
        Assert.That(BlockedCommand.GetOpenCommand(flip), Is.EqualTo("/bz Volcanic Rock"));
        Assert.That(BlockedCommand.GetOpenLabel(flip), Is.EqualTo(" §l[bz]§r"));
        Assert.That(BlockedCommand.SupportsFlipOptions(flip), Is.False);
    }

    [Test]
    public void PrepareBlockedOutputKeepsProfitOrderAfterCollapsingEntries()
    {
        var highProfit = Blocked("high", 5_000_000, DateTime.UtcNow.AddMinutes(-1));
        var recentLowProfit = Blocked("low", 1_000_000, DateTime.UtcNow);

        var result = BlockedCommand.PrepareBlockedOutput(
            [highProfit, recentLowProfit],
            blocked => blocked.Flip.TargetPrice,
            sortByProfit: true);

        Assert.That(result.Select(display => display.Blocked.Flip.Auction.Uuid),
            Is.EqualTo(new[] { "high", "low" }));
        Assert.That(result.Select(display => display.Profit),
            Is.EqualTo(new long[] { 5_000_000, 1_000_000 }));
    }

    private static MinecraftSocket.BlockedElement Blocked(string uuid, long targetPrice, DateTime now)
    {
        return new MinecraftSocket.BlockedElement
        {
            Flip = new LowPricedAuction
            {
                TargetPrice = targetPrice,
                Auction = new SaveAuction { Uuid = uuid }
            },
            Reason = "minProfit",
            Now = now
        };
    }
}
