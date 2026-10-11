namespace Coflnet.Sky.ModCommands.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using Cassandra;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using NUnit.Framework;
using static Coflnet.Sky.ModCommands.Services.BlockedService;

public class BlockedServiceTests
{
    private static MinecraftSocket.BlockedElement Element(DateTime start) => new()
    {
        Flip = new LowPricedAuction() { Auction = new SaveAuction() { Start = start } },
        Reason = "test"
    };

    [Test]
    public void RecentAuctionIsArchived()
    {
        var now = DateTime.UtcNow;
        Assert.That(BlockedService.ShouldArchive(Element(now.AddMinutes(-10)), now), Is.True);
    }

    [Test]
    public void OldAuctionIsSkipped()
    {
        var now = DateTime.UtcNow;
        Assert.That(BlockedService.ShouldArchive(Element(now.AddHours(-3)), now), Is.False);
    }

    [Test]
    public void MissingFlipOrAuctionIsSkipped()
    {
        var now = DateTime.UtcNow;
        Assert.That(BlockedService.ShouldArchive(null, now), Is.False);
        Assert.That(BlockedService.ShouldArchive(new MinecraftSocket.BlockedElement(), now), Is.False);
        Assert.That(BlockedService.ShouldArchive(new MinecraftSocket.BlockedElement() { Flip = new LowPricedAuction() }, now), Is.False);
    }

    [TestCase("blacklist Cheap item", "blacklist")]
    [TestCase("forced blacklist Cheap item", "forced blacklist")]
    [TestCase("whitelist x", "whitelist")]
    [TestCase("finder FLIPPER", "finder")]
    [TestCase("Error something broke", "error")]
    [TestCase("minProfit", "minProfit")]
    [TestCase("high competition", "high competition")]
    public void ReasonGroups(string reason, string group)
    {
        Assert.That(BlockedService.GetReasonGroup(reason), Is.EqualTo(group));
    }

    [Test]
    public void SummarizeGroupsAndSumsProfit()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rows = new List<BlockedReason>
        {
            new() { Reason = "blacklist x", AuctionUuid = a, TargetPrice = 10_000_000, StartingBid = 6_000_000, FinderType = LowPricedAuction.FinderType.SNIPER },
            new() { Reason = "blacklist y", AuctionUuid = b, TargetPrice = 5_000_000, StartingBid = 4_000_000, FinderType = LowPricedAuction.FinderType.FLIPPER },
            new() { Reason = "minProfit", AuctionUuid = Guid.NewGuid(), TargetPrice = 3_000_000, StartingBid = 2_000_000, FinderType = LowPricedAuction.FinderType.SNIPER },
            new() { Reason = "finder FLIPPER", AuctionUuid = Guid.NewGuid(), TargetPrice = 2_000_000, StartingBid = 3_000_000 },
            new() { Reason = "blacklist z", AuctionUuid = Guid.NewGuid() }, // old row without prices
        };

        var result = BlockedService.Summarize(rows);

        Assert.That(result.Select(r => r.ReasonGroup), Is.EqualTo(new[] { "blacklist", "minProfit", "finder" }));
        var blacklist = result[0];
        Assert.That(blacklist.Count, Is.EqualTo(3));
        Assert.That(blacklist.EstimatedProfitSum, Is.EqualTo(5_000_000));
        Assert.That(blacklist.MaxEstimatedProfit, Is.EqualTo(4_000_000));
        Assert.That(blacklist.ExampleAuctionUuids.Take(2), Is.EqualTo(new[] { a, b }));
        Assert.That(blacklist.ExampleAuctionUuids.Count, Is.LessThanOrEqualTo(3));
        Assert.That(blacklist.ByFinder[LowPricedAuction.FinderType.SNIPER], Is.EqualTo(1));
        Assert.That(result[2].EstimatedProfitSum, Is.EqualTo(-1_000_000));
    }

    [Test]
    public void SummaryJsonUsesFinderNames()
    {
        var json = JsonConvert.SerializeObject(BlockedService.Summarize([new BlockedReason() { Reason = "x", FinderType = LowPricedAuction.FinderType.SNIPER_MEDIAN }]));
        Assert.That(json, Does.Contain("\"SNIPER_MEDIAN\":1"));
        Assert.That(json, Does.Contain("\"estimatedProfitSum\""));
    }

    [Test]
    public void ColumnExistsErrorIsRecognized()
    {
        // wording returned by Scylla 6.2.1 (what production runs on)
        Assert.That(BlockedService.IsColumnExistsError(new global::Cassandra.InvalidQueryException("Invalid column name target_price because it conflicts with an existing column")), Is.True);
        // wording returned by Cassandra 4.1.7 (what scripts/cassandra-smoke.sh runs on)
        Assert.That(BlockedService.IsColumnExistsError(new global::Cassandra.InvalidQueryException("Column with name 'target_price' already exists")), Is.True);
        Assert.That(BlockedService.IsColumnExistsError(new global::Cassandra.InvalidQueryException("unconfigured table blocked_reasons")), Is.False);
        Assert.That(BlockedService.IsColumnExistsError(new Exception("conflicts with an existing column")), Is.False);
    }
}
