using System;
using AwesomeAssertions;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.ModCommands.Services.RealizedProfit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using static Coflnet.Sky.ModCommands.Tests.ModSessionLifesycleTests;

namespace Coflnet.Sky.ModCommands.Tests;

/// <summary>A disconnecting session hands its user to the realized profit stats, and never fails because of it</summary>
[NonParallelizable]
public class ModSessionLifesycleRealizedProfitTests
{
    private bool devModeBefore;
    private TestSocket socket = null!;
    private ModSessionLifesycle lifesycle = null!;
    private RealizedProfitQueue queue = null!;

    [SetUp]
    public void Setup()
    {
        devModeBefore = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        DiHandler.ResetProvider();
        OverrideLifecycleServices();
        queue = new RealizedProfitQueue();
        DiHandler.OverrideService<RealizedProfitQueue, RealizedProfitQueue>(queue);
        socket = new TestSocket();
        lifesycle = new ModSessionLifesycle(socket);
        socket.SetLifecycle(lifesycle);
    }

    private static void OverrideLifecycleServices()
    {
        var flipTracking = Mock.Of<IFlipTrackingService>();
        DiHandler.OverrideService<IIsSold, IIsSold>(Mock.Of<IIsSold>());
        DiHandler.OverrideService<IDelayExemptList, IDelayExemptList>(new DelayExemptionList());
        DiHandler.OverrideService<IFlipTrackingService, IFlipTrackingService>(flipTracking);
        DiHandler.OverrideService<IFlipReceiveTracker, IFlipTrackingService>(flipTracking);
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(Mock.Of<INBT>()));
        DiHandler.OverrideService<FlipperService, FlipperService>(new FlipperService(null, NullLogger<FlipperService>.Instance));
    }

    [TearDown]
    public void Restore() => MinecraftSocket.IsDevMode = devModeBefore;

    private void LogIn(string userId)
    {
        lifesycle.UserId = SelfUpdatingValue<string>.CreateNoUpdate(userId);
        lifesycle.AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { McIds = ["b"] });
        var tier = new Mock<IAccountTierManager>();
        // InvalidateCache zeroes ExpiresAt when another connection takes over, the snapshot must not use it
        tier.SetupGet(t => t.ExpiresAt).Returns(DateTime.UtcNow);
        tier.SetupGet(t => t.LastCalculatedExpiresAt).Returns(new DateTime(2030, 1, 1));
        lifesycle.TierManager = tier.Object;
        socket.SessionInfo.McUuid = "a";
        socket.SessionInfo.SessionTier = AccountTier.PREMIUM;
    }

    [Test]
    public void Dispose_QueuesUserTierAndAccounts()
    {
        LogIn("user-1");

        lifesycle.Dispose();

        queue.Reader.TryRead(out var snapshot).Should().BeTrue();
        snapshot!.UserId.Should().Be("user-1");
        snapshot.Tier.Should().Be((int)AccountTier.PREMIUM);
        snapshot.TierExpiresAt.Should().Be(new DateTime(2030, 1, 1));
        snapshot.McUuids.Should().BeEquivalentTo(["a", "b"]);
    }

    [Test]
    public void Dispose_WithoutLogin_QueuesNothing()
    {
        lifesycle.UserId = SelfUpdatingValue<string>.CreateNoUpdate((string)null!);

        lifesycle.Dispose();

        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public void Dispose_WhenQueueIsUnavailable_DoesNotThrow()
    {
        DiHandler.ResetProvider();
        OverrideLifecycleServices();
        LogIn("user-1");

        var output = new System.IO.StringWriter();
        var before = Console.Out;
        Console.SetOut(output);
        try
        {
            var dispose = () => lifesycle.Dispose();
            dispose.Should().NotThrow();
        }
        finally
        {
            Console.SetOut(before);
        }

        output.ToString().Should().NotContain("Failed to queue realized profit", "a missing queue is skipped silently");
    }
}
