using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Tutorials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using StackExchange.Redis;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarOrderDisplayTests
{
    private Mock<IMinecraftSocket> socket;
    private Mock<ITutorialService> tutorials;
    private readonly List<Response> sent = new();

    [SetUp]
    public void Setup()
    {
        sent.Clear();
        socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.Version).Returns(BazaarOrderDisplay.ClientVersion);
        socket.SetupGet(s => s.UserId).Returns("1");
        socket.SetupGet(s => s.SessionInfo).Returns(new SessionInfo { McName = "Ekwav" });
        socket.SetupGet(s => s.Settings).Returns(new FlipSettings { ModSettings = new ModSettings() });
        socket.Setup(s => s.Send(It.IsAny<Response>())).Callback<Response>(sent.Add);
        tutorials = new Mock<ITutorialService>();
        tutorials.Setup(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object)).Returns(Task.CompletedTask);
        socket.Setup(s => s.GetService<ITutorialService>()).Returns(tutorials.Object);
    }

    private static BazaarOrderSnapshot Snapshot(long revision = 1, int filled = 0) => new() {
        UserId = "1", PlayerName = "Ekwav", Revision = revision, Created = revision == 1,
        ItemNames = new() { ["WHEAT"] = "Wheat" },
        Orders = new() { new() { ItemId = "WHEAT", PlayerName = "Ekwav", Amount = 64,
            Filled = filled, IsEstimate = false, PricePerUnit = 10, IsSell = true, Timestamp = DateTime.UtcNow } }
    };

    private InfoDisplay Display => JsonConvert.DeserializeObject<InfoDisplay>(sent.Last().data);

    [TestCase("1.9.3")]
    [TestCase("2.0.0")]
    [TestCase("2.0.0-pre2")]
    [TestCase("af-2.0.0")]
    public async Task OtherVersionsReceiveNoDisplayOrTutorial(string version)
    {
        socket.SetupGet(s => s.Version).Returns(version);
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot());
        BazaarOrderDisplay.Send(socket.Object);
        Assert.That(sent, Is.Empty);
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(It.IsAny<IMinecraftSocket>()), Times.Never);
    }

    [Test]
    public async Task CreatedEventShowsSlotTwoWithHoverDisableAndTutorial()
    {
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot());
        Assert.That(sent.Single().type, Is.EqualTo("infoDisplay"));
        Assert.That(Display.Id, Is.EqualTo(2));
        Assert.That(Display.Clear, Is.False);
        Assert.That(Display.Lines[0].text, Does.Contain("SELL").And.Contain("Wheat"));
        Assert.That(Display.Lines[0].onClick, Is.EqualTo("/managebazaarorders"));
        Assert.That(Display.Lines[0].hover, Does.Contain("Price per unit"));
        Assert.That(Display.Lines.Last().onClick, Is.EqualTo(BazaarOrderDisplay.DisableCommand));
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object), Times.Once);
    }

    [Test]
    public async Task PartialFillPushUpdatesWithoutMenuAndStaleSnapshotCannotUndoIt()
    {
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot());
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot(3, 16));
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot(2, 4), restore: true);
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(Display.Lines[0].text, Does.Contain("16/64"));
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot(4, 64));
        Assert.That(Display.Lines[0].text, Does.Contain("Filled!"));
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object), Times.Once);
    }

    [Test]
    public async Task OtherAccountsAndMinecraftPlayersDoNotLeakIntoDisplay()
    {
        var state = Snapshot();
        state.UserId = "2";
        await BazaarOrderDisplay.Apply(socket.Object, state);
        Assert.That(sent, Is.Empty);
        state.UserId = "1";
        state.PlayerName = "SomeoneElse";
        state.Orders[0].PlayerName = "SomeoneElse";
        await BazaarOrderDisplay.Apply(socket.Object, state);
        Assert.That(Display.Clear, Is.True);
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object), Times.Never);
    }

    [Test]
    public async Task DisabledPreferenceStillRetainsLatestFillsForReenable()
    {
        socket.Object.Settings.ModSettings.HideBazaarOrderDisplay = true;
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot());
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot(2, 16));
        Assert.That(Display.Clear, Is.True);
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object), Times.Never);
        socket.Object.Settings.ModSettings.HideBazaarOrderDisplay = false;
        BazaarOrderDisplay.Send(socket.Object);
        Assert.That(Display.Lines[0].text, Does.Contain("16/64"));
    }

    [Test]
    public async Task EmptyAuthoritativeStateClearsClaimedOrders()
    {
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot());
        var empty = Snapshot(2);
        empty.Orders.Clear();
        await BazaarOrderDisplay.Apply(socket.Object, empty);
        Assert.That(Display.Clear, Is.True);
    }

    [Test]
    public async Task ReconnectLoadsSavedUserSnapshotWithoutRepeatingTutorial()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        var db = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(db.Object);
        db.Setup(d => d.StringGetAsync((RedisKey)"bazaar:orders:v1:1", CommandFlags.None))
            .ReturnsAsync((RedisValue)JsonConvert.SerializeObject(Snapshot(1, 32)));
        var service = new BazaarSignalSubscriptionService(Mock.Of<IConnectionMultiplexer>(MockBehavior.Strict),
            NullLogger<BazaarSignalSubscriptionService>.Instance, redis.Object);
        await service.RestoreAsync(socket.Object);
        Assert.That(Display.Lines[0].text, Does.Contain("32/64"));
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object), Times.Never);
    }
    [Test]
    public async Task EstimatedCompletionDoesNotClaimConfirmedFilledStatus()
    {
        var estimated = Snapshot(2, 64);
        estimated.Orders[0].IsEstimate = true;
        await BazaarOrderDisplay.Apply(socket.Object, estimated);
        Assert.That(Display.Lines[0].text, Does.Contain("estimate").And.Not.Contain("Filled!"));
        await BazaarOrderDisplay.Apply(socket.Object, Snapshot(3, 64));
        Assert.That(Display.Lines[0].text, Does.Contain("Filled!"));
    }
    private class Backend(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/OrderBook/user/1"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Test]
    public async Task NewModRestoresAuthoritativeStateWhenTheSharedRedisCacheWasLost()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(Mock.Of<IDatabase>());
        using var backend = new Backend(JsonConvert.SerializeObject(Snapshot(4, 48)));
        using var client = new HttpClient(backend) { BaseAddress = new Uri("http://bazaar/") };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient("BazaarOrders")).Returns(client);
        var service = new BazaarSignalSubscriptionService(Mock.Of<IConnectionMultiplexer>(MockBehavior.Strict),
            NullLogger<BazaarSignalSubscriptionService>.Instance, redis.Object, clients.Object);
        await service.RestoreAsync(socket.Object);
        Assert.That(Display.Id, Is.EqualTo(2));
        Assert.That(Display.Lines[0].text, Does.Contain("48/64"));
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object), Times.Never);
    }

    [Test]
    public async Task RedisRestartDuringSubscriptionDoesNotStopTheHost()
    {
        var legacy = new Mock<IConnectionMultiplexer>();
        legacy.Setup(r => r.GetSubscriber(null)).Returns(Mock.Of<ISubscriber>());
        var redis = new Mock<IConnectionMultiplexer>();
        var subscriber = new Mock<ISubscriber>();
        redis.Setup(r => r.GetSubscriber(null)).Returns(subscriber.Object);
        var attempted = new TaskCompletionSource();
        subscriber.Setup(s => s.SubscribeAsync(RedisChannel.Literal(BazaarOrderSnapshot.Channel), CommandFlags.None))
            .Callback(() => attempted.TrySetResult())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis restarting"));
        using var service = new BazaarSignalSubscriptionService(legacy.Object,
            NullLogger<BazaarSignalSubscriptionService>.Instance, redis.Object);
        await service.StartAsync(CancellationToken.None);
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(20);
        Assert.That(service.ExecuteTask.IsCompleted, Is.False);
        await service.StopAsync(CancellationToken.None);
        Assert.That(service.ExecuteTask.IsCompletedSuccessfully, Is.True);
    }

    [TestCase(null)]
    [TestCase("invalid")]
    [TestCase("00-11111111111111111111111111111111-2222222222222222-01")]
    public async Task HudTracesAndCountsStaleSnapshotsWithoutRejectingLegacyTraceMetadata(string traceParent)
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == BazaarOrderDisplay.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add
        };
        ActivitySource.AddActivityListener(listener);
        var current = Snapshot(2, 32);
        current.TraceParent = traceParent;
        var stale = Snapshot(1, 16);
        stale.TraceParent = traceParent;
        var before = BazaarOrderDisplay.Snapshots.WithLabels("stale").Value;
        await BazaarOrderDisplay.Apply(socket.Object, current);
        await BazaarOrderDisplay.Apply(socket.Object, stale);
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(Display.Lines[0].text, Does.Contain("32/64"));
        Assert.That(BazaarOrderDisplay.Snapshots.WithLabels("stale").Value, Is.EqualTo(before + 1));
        Assert.That(spans.Last().GetTagItem("bazaar.result"), Is.EqualTo("stale"));
        if (ActivityContext.TryParse(traceParent, null, out var parent))
            Assert.That(spans.All(s => s.TraceId == parent.TraceId && s.ParentSpanId == parent.SpanId), Is.True);
    }

}
