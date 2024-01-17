using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.FlipTracker.Client.Model;
using Moq;
using NUnit.Framework;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.Commands.MC;

public class DelayHandlerTests
{
    private MockTimeProvider timeProvider;
    private string[] ids;
    private SessionInfo sessionInfo;
    private SelfUpdatingValue<AccountInfo> accountInfo;
    private DelayHandler delayHandler;
    private SpeedCompResult result;
    private FlipInstance flipInstance;
    [SetUp]
    public void Setup()
    {
        timeProvider = new MockTimeProvider();
        ids = new string[] { "hi" };
        var configuration = new Mock<IConfiguration>();
        var flipTrackingService = new Mock<FlipTrackingService>(null, null, configuration.Object, null, null, null, null,null);
        sessionInfo = new SessionInfo() { };
        accountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(() => new AccountInfo() { }).Result;
        result = new SpeedCompResult() { Penalty = 1, MacroedFlips = new(), BoughtWorth = 50_000_000 };
        flipTrackingService.Setup(f => f.GetSpeedComp(ids)).Returns(Task.FromResult(result));
        delayHandler = new DelayHandler(timeProvider, flipTrackingService.Object, sessionInfo, accountInfo, new Random(5));
        flipInstance = new FlipInstance() { Auction = new() { StartingBid = 5 } };
    }

    public async Task RequireMc()
    {
        var summary = await delayHandler.Update(ids, timeProvider.Now);
        Assert.AreEqual(4, summary.Penalty.TotalSeconds, 0.00001);
    }

    public async Task NoDelayWhenNoPenalty()
    {
        result.Penalty = 0;
        var summary = await delayHandler.Update(ids, timeProvider.Now);
        Assert.AreEqual(0, summary.Penalty.TotalSeconds, 0.00001);
        var stopWatch = new Stopwatch();
        await delayHandler.AwaitDelayForFlip(flipInstance);
        await delayHandler.AwaitDelayForFlip(flipInstance);
        await delayHandler.AwaitDelayForFlip(flipInstance);
        Assert.AreEqual(0, stopWatch.Elapsed.TotalSeconds, 0.00001);
    }

    [Test]
    public async Task VariesFlipDelays()
    {
        sessionInfo.VerifiedMc = true;
        var summary = await delayHandler.Update(ids, timeProvider.Now);
        Assert.AreEqual(1, summary.Penalty.TotalSeconds, 0.00001);
        var first = delayHandler.AwaitDelayForFlip(flipInstance);
        var second = delayHandler.AwaitDelayForFlip(flipInstance);
        var third = delayHandler.AwaitDelayForFlip(flipInstance);
        var fourth = delayHandler.AwaitDelayForFlip(flipInstance);
        Assert.IsFalse(first.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        Assert.IsFalse(third.IsCompleted);
        Assert.IsFalse(fourth.IsCompleted);
        timeProvider.TickForward(TimeSpan.FromSeconds(0.15));
        Assert.IsFalse(fourth.IsCompleted);
        timeProvider.TickForward(TimeSpan.FromSeconds(0.065));
        Assert.IsTrue(fourth.IsCompleted);
        Assert.IsTrue(third.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        timeProvider.TickForward(TimeSpan.FromSeconds(0.6));
        Assert.IsTrue(second.IsCompleted);
        Assert.IsFalse(first.IsCompleted);
        timeProvider.TickForward(TimeSpan.FromSeconds(0.25));
        Assert.IsTrue(first.IsCompleted);
    }

    [Test]
    public async Task AntiMacroDelay()
    {
        var summary = await delayHandler.Update(ids, new DateTime());
        Assert.AreEqual(12, summary.Penalty.TotalSeconds, 0.00001);
    }
    [Test]
    public async Task LongAntiMacroDelay()
    {
        result.MacroedFlips = new System.Collections.Generic.List<MacroedFlip>(){
            new ()
            {
                BuyTime = timeProvider.Now - TimeSpan.FromSeconds(10),
                TotalSeconds = 3.6
            },
            new ()
            {
                BuyTime = timeProvider.Now - TimeSpan.FromSeconds(12),
                TotalSeconds = 3.5
            }
        };
        result.Penalty = 0.01;
        sessionInfo.VerifiedMc = true;
        var summary = await delayHandler.Update(ids, timeProvider.Now);
        var delayTask = delayHandler.AwaitDelayForFlip(flipInstance);
        timeProvider.TickForward(TimeSpan.FromSeconds(0.02));
        Assert.IsTrue(delayTask.IsCompleted);
        flipInstance.Auction.StartingBid = 5_000_000;
        flipInstance.MedianPrice = 10_100_100;
        flipInstance.Finder = Core.LowPricedAuction.FinderType.SNIPER_MEDIAN;
        delayTask = delayHandler.AwaitDelayForFlip(flipInstance);
        Assert.IsFalse(delayTask.IsCompleted);
        timeProvider.TickForward(TimeSpan.FromSeconds(1));
        Assert.IsTrue(delayTask.IsCompleted);

    }
}

