namespace Coflnet.Sky.ModCommands.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using FinderContext = Coflnet.Sky.FlipTracker.Client.Model.FinderContext;
using Flip = Coflnet.Sky.FlipTracker.Client.Model.Flip;
using TrackerFinder = Coflnet.Sky.FlipTracker.Client.Model.FinderType;
using static Coflnet.Sky.ModCommands.Services.BlockedService;

public class FlipDryRunServiceTests
{
    private const string Uuid = "abcdef1234567890abcdef1234567890";
    private Mock<IFlipDryRunDataSource> source;
    private Mock<IBlockedService> blocked;
    private FlipDryRunService service;
    private SaveAuction auction;

    [SetUp]
    public void Setup()
    {
        auction = new SaveAuction()
        {
            Uuid = Uuid,
            Tag = "TEST_ITEM",
            ItemName = "Test item",
            StartingBid = 10_000_000,
            Bin = true,
            Start = DateTime.UtcNow.AddMinutes(-1),
            End = DateTime.UtcNow.AddHours(1),
            Tier = Tier.EPIC,
            FlatenedNBT = new(),
            Enchantments = new(),
            Context = new()
        };
        source = new Mock<IFlipDryRunDataSource>();
        source.Setup(s => s.GetAuction(Uuid)).ReturnsAsync(auction);
        source.Setup(s => s.GetContexts(It.IsAny<SaveAuction>())).ReturnsAsync(new List<FinderContext>());
        source.Setup(s => s.GetEstimates(It.IsAny<SaveAuction>())).ReturnsAsync(new List<Flip>());
        blocked = new Mock<IBlockedService>();
        blocked.Setup(b => b.GetBlockedReasons("5", It.IsAny<Guid>())).ReturnsAsync(new List<BlockedReason>());
        service = new FlipDryRunService(source.Object, blocked.Object, NullLogger<FlipDryRunService>.Instance);
    }

    private static FinderContext Context(TrackerFinder finder, Dictionary<string, string> context = null)
        => new(Guid.NewGuid(), finder, DateTime.UtcNow, context ?? new(), new());

    [Test]
    public void ReconstructUsesPreferredFinderAverageTargetAndVolume()
    {
        var contexts = new[]
        {
            Context(TrackerFinder.FLIPPER),
            Context(TrackerFinder.SNIPER, new() { { "refCount", "10" }, { "oldRef", "2" } })
        };
        var estimates = new[]
        {
            new Flip(1, 20_000_000, TrackerFinder.FLIPPER, DateTime.UtcNow),
            new Flip(1, 12_000_000, TrackerFinder.SNIPER, DateTime.UtcNow),
            new Flip(1, 14_000_000, TrackerFinder.SNIPER, DateTime.UtcNow),
        };

        var flip = FlipDryRunService.Reconstruct(auction, contexts, estimates, LowPricedAuction.FinderType.SNIPER);

        Assert.That(flip.Finder, Is.EqualTo(LowPricedAuction.FinderType.SNIPER));
        Assert.That(flip.TargetPrice, Is.EqualTo(13_000_000));
        Assert.That(flip.DailyVolume, Is.EqualTo(6f));
        Assert.That(flip.AdditionalProps["refCount"], Is.EqualTo("10"));
    }

    [Test]
    public void ReconstructWithoutPreferenceUsesFirstContext()
    {
        var flip = FlipDryRunService.Reconstruct(auction, [Context(TrackerFinder.FLIPPER)],
            [new Flip(1, 15_000_000, TrackerFinder.FLIPPER, DateTime.UtcNow)]);
        Assert.That(flip.Finder, Is.EqualTo(LowPricedAuction.FinderType.FLIPPER));
        Assert.That(flip.TargetPrice, Is.EqualTo(15_000_000));
        Assert.That(flip.DailyVolume, Is.EqualTo(1f));
    }

    /// <summary>
    /// The inline code in BlockedCommand dereferenced the context before the null check and threw a NullReferenceException
    /// </summary>
    /// <summary>
    /// The tracker enum has no underscore in SNIPERMEDIAN, the old Enum.Parse threw for it
    /// </summary>
    [Test]
    public void ReconstructMapsSniperMedian()
    {
        var flip = FlipDryRunService.Reconstruct(auction, [Context(TrackerFinder.SNIPERMEDIAN)], null, LowPricedAuction.FinderType.SNIPER_MEDIAN);
        Assert.That(flip.Finder, Is.EqualTo(LowPricedAuction.FinderType.SNIPER_MEDIAN));
    }

    [Test]
    public void ReconstructWithoutContextFallsBackToUser()
    {
        var flip = FlipDryRunService.Reconstruct(auction, new List<FinderContext>(), null);
        Assert.That(flip.Finder, Is.EqualTo(LowPricedAuction.FinderType.USER));
        Assert.That(flip.TargetPrice, Is.EqualTo(auction.StartingBid));
        Assert.That(flip.DailyVolume, Is.EqualTo(1f));
        Assert.That(FlipDryRunService.Reconstruct(auction, null, null).Finder, Is.EqualTo(LowPricedAuction.FinderType.USER));
    }

    private void SetupFlip(TrackerFinder finder, int target)
    {
        source.Setup(s => s.GetContexts(It.IsAny<SaveAuction>())).ReturnsAsync(new List<FinderContext>
        {
            Context(finder, new() { { "refCount", "10" }, { "oldRef", "2" } })
        });
        source.Setup(s => s.GetEstimates(It.IsAny<SaveAuction>())).ReturnsAsync(new List<Flip>
        {
            new Flip(1, target, finder, DateTime.UtcNow)
        });
    }

    [Test]
    public async Task MinProfitBlocksSmallProfit()
    {
        SetupFlip(TrackerFinder.SNIPERMEDIAN, 12_500_000);
        source.Setup(s => s.GetSettings("5")).ReturnsAsync(new FlipSettings()
        {
            MinProfit = 5_000_000,
            AllowedFinders = LowPricedAuction.FinderType.FLIPPER_AND_SNIPERS
        });

        var result = await service.Evaluate("5", Uuid);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.Reason, Is.EqualTo("minProfit"));
        Assert.That(result.FinderBlocked, Is.False);
        Assert.That(result.FlipSource, Is.EqualTo("finder_context"));
        Assert.That(result.EstimatedProfit, Is.InRange(1_000_000, 3_000_000));
        Assert.That(result.SettingsSummary.MinProfit, Is.EqualTo(5_000_000));
        Assert.That(result.Warnings, Has.Some.StartsWith("not evaluated: purse"));
    }

    [Test]
    public async Task MatchesWhenProfitIsEnough()
    {
        SetupFlip(TrackerFinder.SNIPERMEDIAN, 12_500_000);
        source.Setup(s => s.GetSettings("5")).ReturnsAsync(new FlipSettings()
        {
            MinProfit = 100_000,
            AllowedFinders = LowPricedAuction.FinderType.FLIPPER_AND_SNIPERS
        });

        var result = await service.Evaluate("5", Uuid);

        Assert.That(result.Matches, Is.True, result.Reason);
        Assert.That(result.Reason, Is.EqualTo("general filter"));
    }

    [Test]
    public async Task UnsetFindersBlockFlipperFinder()
    {
        SetupFlip(TrackerFinder.FLIPPER, 12_500_000);
        source.Setup(s => s.GetSettings("5")).ReturnsAsync(new FlipSettings());

        var result = await service.Evaluate("5", Uuid);

        Assert.That(result.FinderBlocked, Is.True);
        Assert.That(result.Matches, Is.False);
        Assert.That(result.Reason, Is.EqualTo("finder FLIPPER"));
        Assert.That(JsonConvert.SerializeObject(result), Does.Contain("\"finder\":\"FLIPPER\""));
    }

    [Test]
    public async Task NoStoredSettingsAddsWarning()
    {
        SetupFlip(TrackerFinder.SNIPER, 12_500_000);
        source.Setup(s => s.GetSettings("5")).ReturnsAsync((FlipSettings)null);

        var result = await service.Evaluate("5", Uuid);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.Warnings, Does.Contain("no settings stored"));
    }

    [Test]
    public async Task FallsBackToSniperEstimateWithoutContext()
    {
        source.Setup(s => s.GetSniperEstimate(It.IsAny<SaveAuction>())).ReturnsAsync(new Sniper.Client.Model.PriceEstimate() { Median = 12_500_000, Volume = 8 });
        source.Setup(s => s.GetSettings("5")).ReturnsAsync(new FlipSettings() { AllowedFinders = LowPricedAuction.FinderType.FLIPPER_AND_SNIPERS });

        var result = await service.Evaluate("5", Uuid);

        Assert.That(result.FlipSource, Is.EqualTo("sniper_estimate_now"));
        Assert.That(result.TargetPrice, Is.EqualTo(12_500_000));
        Assert.That(result.Warnings, Has.Some.Contain("current, not"));
    }

    [Test]
    public async Task UnknownAuctionReturnsNull()
    {
        source.Setup(s => s.GetAuction("ffffffffffffffffffffffffffffffff")).ReturnsAsync((SaveAuction)null);
        Assert.That(await service.Evaluate("5", "ffffffffffffffffffffffffffffffff"), Is.Null);
    }
}
