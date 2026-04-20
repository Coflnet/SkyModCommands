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
}