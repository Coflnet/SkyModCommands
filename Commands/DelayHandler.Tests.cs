using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.FlipTracker.Client.Model;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class DelayHandlerTests
{
    MockTimeProvider timeProvider;
    string[] ids;
    SessionInfo sessionInfo;
    DelayHandler delayHandler;
    SpeedCompResult result;
    [SetUp]
    public void Setup()
    {
        timeProvider = new MockTimeProvider();
        ids = new string[] { "hi" };
        var flipTrackingService = new Mock<FlipTrackingService>(null);
        result = new SpeedCompResult() { Penalty = 1 };
        flipTrackingService.Setup(f => f.GetSpeedComp(ids)).Returns(Task.FromResult(result));
        sessionInfo = new SessionInfo();
        delayHandler = new DelayHandler(timeProvider, flipTrackingService.Object, sessionInfo, new System.Random(5));
    }

    public async Task RequireMc()
    {
        var summary = await delayHandler.Update(ids, System.DateTime.UtcNow);
        Assert.AreEqual(4, summary.Penalty.TotalSeconds, 0.00001);
    }

    public async Task NoDelayWhenNoPenalty()
    {
        result.Penalty = 0;
        var summary = await delayHandler.Update(ids, System.DateTime.UtcNow);
        Assert.AreEqual(0, summary.Penalty.TotalSeconds, 0.00001);
        var stopWatch = new Stopwatch();
        await delayHandler.AwaitDelayForFlip();
        await delayHandler.AwaitDelayForFlip();
        await delayHandler.AwaitDelayForFlip();
        Assert.AreEqual(0, stopWatch.Elapsed.TotalSeconds, 0.00001);
    }

    [Test]
    public async Task VariesFlipDelays()
    {
        sessionInfo.VerifiedMc = true;
        var summary = await delayHandler.Update(ids, System.DateTime.UtcNow);
        Assert.AreEqual(1, summary.Penalty.TotalSeconds, 0.00001);
        var first = delayHandler.AwaitDelayForFlip();
        var second = delayHandler.AwaitDelayForFlip();
        var third = delayHandler.AwaitDelayForFlip();
        var fourth = delayHandler.AwaitDelayForFlip();
        Assert.IsFalse(first.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        Assert.IsFalse(third.IsCompleted);
        Assert.IsFalse(fourth.IsCompleted);
        timeProvider.TickForward(System.TimeSpan.FromSeconds(0.2));
        Assert.IsFalse(fourth.IsCompleted);
        timeProvider.TickForward(System.TimeSpan.FromSeconds(0.15));
        Assert.IsTrue(fourth.IsCompleted);
        Assert.IsFalse(third.IsCompleted);
        timeProvider.TickForward(System.TimeSpan.FromSeconds(0.15));
        Assert.IsTrue(third.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        timeProvider.TickForward(System.TimeSpan.FromSeconds(0.35));
        Assert.IsTrue(second.IsCompleted);
        Assert.IsFalse(first.IsCompleted);
        timeProvider.TickForward(System.TimeSpan.FromSeconds(0.25));
        Assert.IsTrue(first.IsCompleted);
    }
}

