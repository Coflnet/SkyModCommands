using System;
using System.Collections.Generic;
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

public class FlipProcesserMatchReportTests
{
    private ErrorRecordingSocket socket;
    private ModSessionLifesycle lifesycle;
    private Mock<IFlipMatchTracker> tracker;

    [SetUp]
    public async Task Setup()
    {
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
        socket = new ErrorRecordingSocket();
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

    [Test]
    public void ApiRelativeSecondsForApiFlipUsesFindTime()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 10, DateTimeKind.Utc);
        var flip = Flip();
        flip.Auction.FindTime = now.AddSeconds(-3);

        var result = FlipProcesser.ApiRelativeSeconds(flip, new ApiUpdateClock(), now);

        result.Should().Be(("api", 3d));
    }

    [Test]
    public void ApiRelativeSecondsForPreApiIsNegativeBeforeUpdate()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 5, DateTimeKind.Utc);
        var clock = new ApiUpdateClock();
        clock.Set(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)); // next update 12:01:00 -> Start+55s
        var flip = Flip();
        flip.Auction.Start = start;
        flip.Auction.Context = new() { { "pre-api", "true" } };
        var firstUpdate = clock.FirstUpdateAfter(start).Value;

        var result = FlipProcesser.ApiRelativeSeconds(flip, clock, firstUpdate.AddSeconds(-5));

        result.Should().Be(("preapi", -5d));
    }

    [Test]
    public void ApiRelativeSecondsForPreApiWithUnknownClockIsNull()
    {
        var flip = Flip();
        flip.Auction.Context = new() { { "pre-api", "true" } };

        FlipProcesser.ApiRelativeSeconds(flip, new ApiUpdateClock(), DateTime.UtcNow).Should().BeNull();
        FlipProcesser.ApiRelativeSeconds(flip, null, DateTime.UtcNow).Should().BeNull();
    }

    /// <summary>
    /// The test socket has no websocket session, so the base error logging cannot read the query string
    /// </summary>
    private sealed class ErrorRecordingSocket : TestSocket
    {
        public List<Exception> Errors = [];
        public override string Error(Exception exception, string message = null, string additionalLog = null)
        {
            Errors.Add(exception);
            return string.Empty;
        }
    }
}
