using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using static Coflnet.Sky.Core.LowPricedAuction;
using static Coflnet.Sky.ModCommands.Tests.ModSessionLifesycleTests;

namespace Coflnet.Sky.Commands.MC;

[NonParallelizable]
public class FlipProcesserMatchReportTests
{
    private bool devModeBefore;
    private TestSocket socket;
    private ModSessionLifesycle lifesycle;
    private Mock<IFlipMatchTracker> tracker;

    [TearDown]
    public void RestoreDevMode() => MinecraftSocket.IsDevMode = devModeBefore;

    [SetUp]
    public async Task Setup()
    {
        // CI hosts are not in dev mode, test the same way everywhere
        devModeBefore = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        DiHandler.ResetProvider();
        var isSold = new Mock<IIsSold>();
        isSold.Setup(s => s.IsSold(It.IsAny<string>())).Returns(false);
        var flipTracking = new Mock<IFlipTrackingService>();
        tracker = new Mock<IFlipMatchTracker>();
        DiHandler.OverrideService<IIsSold, IIsSold>(isSold.Object);
        DiHandler.OverrideService<IDelayExemptList, IDelayExemptList>(new DelayExemptionList());
        DiHandler.OverrideService<IFlipTrackingService, IFlipTrackingService>(flipTracking.Object);
        DiHandler.OverrideService<IFlipReceiveTracker, IFlipTrackingService>(flipTracking.Object);
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(new Mock<INBT>().Object));
        DiHandler.OverrideService<FlipperService, FlipperService>(new FlipperService(null, NullLogger<FlipperService>.Instance));
        DiHandler.OverrideService<IFlipMatchTracker, IFlipMatchTracker>(tracker.Object);
        socket = new TestSocket();
        lifesycle = new ModSessionLifesycle(socket);
        socket.SetLifecycle(lifesycle);
        socket.SessionInfo.FlipsEnabled = true;
        await ApplySettings(new FlipSettings());
    }

    private async Task ApplySettings(FlipSettings settings)
    {
        settings.BlockHighCompetitionFlips = false;
        settings.MinVolume = 0;
        settings.BlackList ??= [];
        settings.WhiteList ??= [];
        settings.AllowedFinders = FinderType.SNIPER_MEDIAN;
        settings.Visibility = new();
        settings.ModSettings = new();
        lifesycle.FlipSettings = await SelfUpdatingValue<FlipSettings>.CreateNoUpdate(() => settings);
        await lifesycle.ApplyFlipSettings(settings, null);
    }

    private static LowPricedAuction Flip(string tag = "TANK_WITHER_BOOTS") => new()
    {
        Auction = new()
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Tag = tag,
            StartingBid = 5_300_000,
            UId = Random.Shared.Next(1_000_000, 9_999_999),
            Bin = true,
        },
        Finder = FinderType.SNIPER_MEDIAN,
        AdditionalProps = new(),
        TargetPrice = 6_500_000
    };

    [Test]
    public async Task MatchingFlipIsReportedOnceWithSessionTier()
    {
        socket.SessionInfo.SessionTier = AccountTier.PREMIUM_PLUS;
        var flip = Flip();

        await lifesycle.FlipProcessor.ProcessFlip(flip);

        tracker.Verify(t => t.ReportMatch(flip.UId, AccountTier.PREMIUM_PLUS), Times.Once);
        socket.Flips.Should().HaveCount(1);
    }

    [Test]
    public async Task BlacklistedFlipIsNotReported()
    {
        socket.SessionInfo.SessionTier = AccountTier.PREMIUM_PLUS;
        var settings = new FlipSettings { BlackList = [new() { ItemTag = "TANK_WITHER_BOOTS" }] };
        await ApplySettings(settings);

        await lifesycle.FlipProcessor.ProcessFlip(Flip());

        tracker.Verify(t => t.ReportMatch(It.IsAny<long>(), It.IsAny<AccountTier>()), Times.Never);
        socket.Flips.Should().BeEmpty();
    }

    [Test]
    public async Task FreeSessionIsNotReported()
    {
        socket.SessionInfo.SessionTier = AccountTier.NONE;

        await lifesycle.FlipProcessor.ProcessFlip(Flip());

        tracker.Verify(t => t.ReportMatch(It.IsAny<long>(), It.IsAny<AccountTier>()), Times.Never);
    }

    [Test]
    public async Task BatchReportsAllMatchesBeforeFirstFairnessWait()
    {
        socket.SessionInfo.SessionTier = AccountTier.PREMIUM_PLUS;
        var reportedAtFirstWait = -1;
        var reported = 0;
        tracker.Setup(t => t.ReportMatch(It.IsAny<long>(), It.IsAny<AccountTier>())).Callback(() => reported++);
        var delay = new Mock<IDelayHandler>();
        delay.Setup(d => d.IsLikelyBot(It.IsAny<FlipInstance>())).Returns(false);
        delay.Setup(d => d.AwaitDelayForFlip(It.IsAny<FlipInstance>()))
            .Returns(() =>
            {
                if (reportedAtFirstWait < 0)
                    reportedAtFirstWait = reported;
                return Task.FromResult(DateTime.UtcNow);
            });
        var spam = new Mock<ISpamController>();
        spam.Setup(s => s.ShouldBeSent(It.IsAny<FlipInstance>())).Returns(true);
        var processor = new FlipProcesser(socket, spam.Object, delay.Object);

        await processor.ProcessBatch([Flip(), Flip()]);

        reportedAtFirstWait.Should().Be(2);
        socket.Flips.Should().HaveCount(2);
    }

    private static (FlipInstance instance, LowPricedAuction lp) ExemptCandidate(bool preApi = false)
    {
        var auction = new SaveAuction { Uuid = Guid.NewGuid().ToString("N"), Tag = "TANK_WITHER_BOOTS", Start = DateTime.UtcNow };
        if (preApi)
            auction.Context = new Dictionary<string, string> { ["pre-api"] = "true" };
        var lp = new LowPricedAuction { Auction = auction, AdditionalProps = new Dictionary<string, string> { ["key"] = "k" } };
        return (new FlipInstance { Auction = auction }, lp);
    }

    [TestCase(0.1, false, false, FlipProcesser.InstantReason.Exempt)]
    [TestCase(0.5, false, false, FlipProcesser.InstantReason.None)]
    [TestCase(0.5, false, true, FlipProcesser.InstantReason.LikelyBot)]
    [TestCase(0.1, false, true, FlipProcesser.InstantReason.Exempt)]
    [TestCase(0.1, true, false, FlipProcesser.InstantReason.None)]
    [TestCase(0.5, true, true, FlipProcesser.InstantReason.None)]
    public void InstantReasonMatchesTheFormerCondition(double draw, bool preApi, bool bot, FlipProcesser.InstantReason expected)
    {
        var exempt = new DelayExemptionList();
        exempt.Exemptions.Add(("TANK_WITHER_BOOTS", "k"));
        var handler = new Mock<IDelayHandler>();
        handler.Setup(h => h.IsLikelyBot(It.IsAny<FlipInstance>())).Returns(bot);
        var (instance, lp) = ExemptCandidate(preApi);

        FlipProcesser.GetInstantReason(draw, lp, instance, exempt, handler.Object).Should().Be(expected);
    }

    [Test]
    public void ExemptPathNeverAsksTheBotCheck()
    {
        var exempt = new Mock<IDelayExemptList>();
        exempt.Setup(e => e.IsExempt(It.IsAny<LowPricedAuction>())).Returns(true);
        var handler = new Mock<IDelayHandler>();
        var (instance, lp) = ExemptCandidate();

        FlipProcesser.GetInstantReason(0.1, lp, instance, exempt.Object, handler.Object);

        exempt.Verify(e => e.IsExempt(lp), Times.Once());
        handler.Verify(h => h.IsLikelyBot(It.IsAny<FlipInstance>()), Times.Never());
    }

    [TestCase(0.33)]
    [TestCase(0.9)]
    public void ExemptListIsNotAskedWithoutALowDraw(double draw)
    {
        var exempt = new Mock<IDelayExemptList>();
        var handler = new Mock<IDelayHandler>();
        var (instance, lp) = ExemptCandidate();

        FlipProcesser.GetInstantReason(draw, lp, instance, exempt.Object, handler.Object);

        exempt.Verify(e => e.IsExempt(It.IsAny<LowPricedAuction>()), Times.Never());
        handler.Verify(h => h.IsLikelyBot(instance), Times.Once());
    }

    [Test]
    public void PreApiIsOnlyCheckedAfterTheDecisionSides()
    {
        var exempt = new Mock<IDelayExemptList>();
        exempt.Setup(e => e.IsExempt(It.IsAny<LowPricedAuction>())).Returns(true);
        var handler = new Mock<IDelayHandler>();
        var (instance, lp) = ExemptCandidate(preApi: true);

        var reason = FlipProcesser.GetInstantReason(0.1, lp, instance, exempt.Object, handler.Object);

        reason.Should().Be(FlipProcesser.InstantReason.None);
        exempt.Verify(e => e.IsExempt(lp), Times.Once());
    }

    [Test]
    public void NotListedItemIsNeverExemptEvenOnLuckyDraw()
    {
        var (instance, lp) = ExemptCandidate();
        lp.AdditionalProps["key"] = "other";
        var handler = new Mock<IDelayHandler>();

        FlipProcesser.GetInstantReason(0.0, lp, instance, new DelayExemptionList(), handler.Object).Should().Be(FlipProcesser.InstantReason.None);
    }

    [Test]
    public void SelectInstantCountsNoBedAndExemptFlips()
    {
        DiHandler.GetService<IDelayExemptList>().Exemptions.Add(("TANK_WITHER_BOOTS", "k"));
        var processor = CreateProcessor();
        var flips = Enumerable.Range(0, 400).Select(_ => ExemptCandidate()).ToList();

        var instant = processor.SelectInstant(flips);

        processor.NoBedFlipCount.Should().Be(400);
        processor.ExemptInstantFlipCount.Should().Be(instant.Count);
        instant.Count.Should().BeInRange(60, 200); // 33% of 400
    }

    [Test]
    public void SelectInstantDoesNotCountLikelyBotAsExempt()
    {
        var delay = new Mock<IDelayHandler>();
        delay.Setup(d => d.IsLikelyBot(It.IsAny<FlipInstance>())).Returns(true);
        var processor = new FlipProcesser(socket, new Mock<ISpamController>().Object, delay.Object);
        var flips = Enumerable.Range(0, 5).Select(_ => ExemptCandidate()).ToList();

        var instant = processor.SelectInstant(flips);

        instant.Should().HaveCount(5);
        processor.NoBedFlipCount.Should().Be(5);
        processor.ExemptInstantFlipCount.Should().Be(0);
    }

    private FlipProcesser CreateProcessor()
    {
        var delay = new Mock<IDelayHandler>();
        delay.Setup(d => d.AwaitDelayForFlip(It.IsAny<FlipInstance>())).ReturnsAsync(DateTime.UtcNow);
        var spam = new Mock<ISpamController>();
        spam.Setup(s => s.ShouldBeSent(It.IsAny<FlipInstance>())).Returns(true);
        return new FlipProcesser(socket, spam.Object, delay.Object);
    }

    [Test]
    public async Task BatchSurvivesFlipThatThrowsInFilter()
    {
        socket.SessionInfo.SessionTier = AccountTier.PREMIUM_PLUS;
        var broken = Flip();
        broken.Auction = null;
        var good = Flip();

        await CreateProcessor().ProcessBatch([broken, good]);

        socket.Flips.Should().ContainSingle();
        socket.Errors.Should().ContainSingle();
    }

    [Test]
    public async Task BatchDedupesSameAuctionUId()
    {
        socket.SessionInfo.SessionTier = AccountTier.PREMIUM_PLUS;
        var first = Flip();
        var second = Flip();
        second.Auction.UId = first.Auction.UId;
        second.Auction.Uuid = first.Auction.Uuid;

        await CreateProcessor().ProcessBatch([first, second]);

        socket.Flips.Should().ContainSingle();
        tracker.Verify(t => t.ReportMatch(first.UId, AccountTier.PREMIUM_PLUS), Times.Once);
    }
}
