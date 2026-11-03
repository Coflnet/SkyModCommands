using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using static Coflnet.Sky.ModCommands.Tests.ModSessionLifesycleTests;

namespace Coflnet.Sky.ModCommands.Tests;

/// <summary>
/// A disposed session must not keep looping, subscribing or registering anything
/// </summary>
[NonParallelizable]
public class SessionLifetimeTests
{
    private const string StringId = "AAECAwQFBgcICQoLDA0ODw==";
    private bool devModeBefore;
    private TestSocket socket;
    private ExposedLifecycle lifesycle;

    [SetUp]
    public void Setup()
    {
        devModeBefore = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        DiHandler.ResetProvider();
        var flipTracking = Mock.Of<IFlipTrackingService>();
        var isSold = Mock.Of<IIsSold>();
        DiHandler.OverrideService<IIsSold, IIsSold>(isSold);
        DiHandler.OverrideService<IDelayExemptList, IDelayExemptList>(new DelayExemptionList());
        DiHandler.OverrideService<IFlipTrackingService, IFlipTrackingService>(flipTracking);
        DiHandler.OverrideService<IFlipReceiveTracker, IFlipTrackingService>(flipTracking);
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(Mock.Of<INBT>()));
        DiHandler.OverrideService<FlipperService, FlipperService>(new FlipperService(null, NullLogger<FlipperService>.Instance));
        socket = new TestSocket();
        lifesycle = new ExposedLifecycle(socket);
        socket.SetLifecycle(lifesycle);
        lifesycle.UserId = SelfUpdatingValue<string>.CreateNoUpdate((string)null);
    }

    [TearDown]
    public void Restore() => MinecraftSocket.IsDevMode = devModeBefore;

    [Test]
    public async Task LoginPromptStopsAfterOneHour()
    {
        var delays = new List<TimeSpan>();
        lifesycle.LoginPromptDelay = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

        await lifesycle.RunLoginPromptLoop(StringId).WaitAsync(TimeSpan.FromSeconds(5));

        delays.Should().NotBeEmpty();
        delays.Aggregate(TimeSpan.Zero, (a, b) => a + b).Should().Be(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task LoginPromptStopsWhenSessionIsDisposed()
    {
        lifesycle.LoginPromptDelay = (delay, token) => Task.Delay(Timeout.Infinite, token);
        var loop = lifesycle.RunLoginPromptLoop(StringId);
        loop.IsCompleted.Should().BeFalse();

        lifesycle.Dispose();

        await loop.WaitAsync(TimeSpan.FromSeconds(5));
        lifesycle.SessionToken.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public async Task LoginPromptStopsWhenUserLogsIn()
    {
        var calls = 0;
        lifesycle.LoginPromptDelay = async (delay, _) =>
        {
            calls++;
            await lifesycle.UserId.Update("5");
        };

        await lifesycle.RunLoginPromptLoop(StringId).WaitAsync(TimeSpan.FromSeconds(5));

        calls.Should().Be(1);
    }

    [Test]
    public async Task SetupAfterDisposeCreatesNothing()
    {
        lifesycle.Dispose();

        await lifesycle.SetupConnectionSettings(StringId);

        lifesycle.PingTimer.Should().BeNull();
        lifesycle.TierManager.Should().BeNull();
    }

    [Test]
    public async Task DisposeWhileSubscribingToSettingsReleasesTheCreatedValues()
    {
        var gate = new TaskCompletionSource<Settings.Client.Client.ApiResponse<string>>();
        var api = new Mock<ISettingsApi>();
        api.Setup(a => a.GetSettingWithHttpInfoAsync("7", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string key, int _, CancellationToken _) => key == "accountInfo"
                ? gate.Task
                : Task.FromResult(new Settings.Client.Client.ApiResponse<string>(HttpStatusCode.NoContent, null)));
        var settings = new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object);
        DiHandler.OverrideService<SettingsService, SettingsService>(settings);

        var subscribing = lifesycle.SubscribeSettings("7");
        lifesycle.Dispose();
        gate.SetResult(new(HttpStatusCode.OK, JsonConvert.SerializeObject(new AccountInfo { UserId = "7" })));
        await subscribing.WaitAsync(TimeSpan.FromSeconds(5));

        lifesycle.AccountInfo.Value.Should().BeNull("a value created after dispose has to be disposed again");
        lifesycle.FlipSettings.Should().BeNull();
    }

    [Test]
    public async Task SettingsSubscriptionCleanupHandlesFaultedAndMissingTasks()
    {
        SettingsService.UnsubscribeQuietly(null);
        SettingsService.UnsubscribeQuietly(Task.FromException<StackExchange.Redis.ChannelMessageQueue>(new Exception("x")));
        SettingsService.UnsubscribeQuietly(Task.FromResult<StackExchange.Redis.ChannelMessageQueue>(null));
        await Task.Delay(50);
    }

    private class ExposedLifecycle : ModSessionLifesycle
    {
        public ExposedLifecycle(MinecraftSocket socket) : base(socket) { }
        public Task SubscribeSettings(string userId) => SubToSettings(userId);
    }
}
