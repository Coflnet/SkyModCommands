using System;
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

/// <summary>Values a session replaces or creates late have to be disposed, the login keeps its user id</summary>
[NonParallelizable]
public class SessionReviewFixesTests
{
    private bool devModeBefore;
    private TestSocket socket = null!;

    [SetUp]
    public void Setup()
    {
        devModeBefore = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        DiHandler.ResetProvider();
        var flipTracking = Mock.Of<IFlipTrackingService>();
        DiHandler.OverrideService<IIsSold, IIsSold>(Mock.Of<IIsSold>());
        DiHandler.OverrideService<IDelayExemptList, IDelayExemptList>(new DelayExemptionList());
        DiHandler.OverrideService<IFlipTrackingService, IFlipTrackingService>(flipTracking);
        DiHandler.OverrideService<IFlipReceiveTracker, IFlipTrackingService>(flipTracking);
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(Mock.Of<INBT>()));
        DiHandler.OverrideService<FlipperService, FlipperService>(new FlipperService(null, NullLogger<FlipperService>.Instance));
        socket = new TestSocket();
    }

    [TearDown]
    public void Restore() => MinecraftSocket.IsDevMode = devModeBefore;

    [Test]
    public void ReplaceValueDisposesThePreviousValue()
    {
        var old = SelfUpdatingValue<string>.CreateNoUpdate("old");
        var current = old;
        var created = SelfUpdatingValue<string>.CreateNoUpdate("new");

        ModSessionLifesycle.ReplaceValue(ref current, created);

        current.Should().BeSameAs(created);
        created.Value.Should().Be("new");
        old.Value.Should().BeNull();
    }

    [Test]
    public void DisposingTheSessionDisposesThePrivacySettings()
    {
        var session = new InventoryModSession(socket);
        socket.SetLifecycle(session);
        session.PrivacySettings = SelfUpdatingValue<PrivacySettings>.CreateNoUpdate(new PrivacySettings());
        var settings = session.PrivacySettings;

        session.Dispose();

        settings.Value.Should().BeNull("the AfterChange handler would keep the session alive otherwise");
    }

    [Test]
    public async Task DisposedInventorySessionCreatesNoPrivacySettings()
    {
        var session = new ExposedInventorySession(socket);
        socket.SetLifecycle(session);
        session.Dispose();

        await session.Subscribe("7");

        session.PrivacySettings.Should().BeNull();
    }

    [Test]
    public async Task LoginPromptKeepsTheUserIdValueAlive()
    {
        var api = new Mock<ISettingsApi>();
        api.Setup(a => a.GetSettingWithHttpInfoAsync(It.IsAny<string>(), "userId", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Settings.Client.Client.ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject("5")));
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object));
        var session = new NoSetupSession(socket);
        socket.SetLifecycle(session);
        session.UserId = SelfUpdatingValue<string>.CreateNoUpdate((string)null!);
        var previous = session.UserId;

        var loginSocket = new Mock<IMinecraftSocket>();
        loginSocket.SetupGet(s => s.SessionInfo).Returns(socket.SessionInfo);
        loginSocket.SetupGet(s => s.sessionLifesycle).Returns(session);

        await loginSocket.Object.SendLoginPrompt();

        session.UserId.Value.Should().Be("5");
        previous.Value.Should().BeNull("the replaced value is disposed");
    }

    [Test]
    public async Task UpdateOnADisposedValueDoesNotWrite()
    {
        var api = new Mock<ISettingsApi>();
        api.Setup(a => a.GetSettingWithHttpInfoAsync("9", "accountSettings", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Settings.Client.Client.ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject(new AccountSettings())));
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object));
        var value = await SelfUpdatingValue<AccountSettings>.Create("9", "accountSettings", () => new());
        value.Dispose();

        await value.Update(new AccountSettings());
        await value.Update(v => { });
        await value.Update();

        api.Verify(a => a.UpdateSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UpdateOnADisposedLocalValueKeepsItEmpty()
    {
        var value = SelfUpdatingValue<string>.CreateNoUpdate("a");
        value.Dispose();

        await value.Update("b");

        value.Value.Should().BeNull();
    }

    [Test]
    public async Task UpdateWithActionOnAnEmptyValueDoesNotInvokeTheAction()
    {
        var value = SelfUpdatingValue<string>.CreateNoUpdate((string)null!);
        var called = false;

        await value.Update(v => called = true);

        called.Should().BeFalse("a value disposed in between has no state to modify");
    }

    [Test]
    public void DelayHandlerReadsTheCurrentAccountInfo()
    {
        var first = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { ShadinessLevel = 90 });
        var session = new NoSetupSession(socket);
        socket.SetLifecycle(session);
        session.AccountInfo = first;
        var handler = session.DelayHandler;

        ModSessionLifesycle.ReplaceValue(ref session.AccountInfo, SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { ShadinessLevel = 90 }));
        first.Value.Should().BeNull();

        // a handler bound to the disposed instance would lose the info, the lifecycle one keeps reading the current value
        session.DelayHandler.Should().BeSameAs(handler);
        var read = typeof(DelayHandler).GetField("getAccountInfo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((Func<AccountInfo>)read.GetValue(handler))().ShadinessLevel.Should().Be(90);
    }

    [Test]
    public async Task ProxySyncFollowsReplacedValuesAndStopsWhenStopped()
    {
        var session = new NoSetupSession(socket);
        socket.SetLifecycle(session);
        session.AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo());
        session.FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings());
        var sent = 0;
        var sync = ProxyReqSyncCommand.WireStateSync(socket, session, () => sent++);

        ModSessionLifesycle.ReplaceValue(ref session.AccountInfo, SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo()));
        ModSessionLifesycle.ReplaceValue(ref session.FlipSettings, SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings()));
        session.RaiseValuesReplaced();
        session.RaiseValuesReplaced();
        await session.AccountInfo.Update(new AccountInfo());
        sent.Should().Be(1, "the handler follows the replaced account info once");
        await session.FlipSettings.Update(new FlipSettings());
        sent.Should().Be(2, "the handler follows the replaced flip settings once");

        sync.Stop();
        await session.AccountInfo.Update(new AccountInfo());
        await session.FlipSettings.Update(new FlipSettings());
        ModSessionLifesycle.ReplaceValue(ref session.AccountInfo, SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo()));
        sync.Attach(); // a late replace notification must not hook a stopped sync
        await session.AccountInfo.Update(new AccountInfo());
        sent.Should().Be(2, "nothing is sent after Stop");
    }

    [Test]
    public async Task ProxySyncKeepsSelfOriginatedChangesPreventedAndSendsTheNewValue()
    {
        var session = new NoSetupSession(socket);
        socket.SetLifecycle(session);
        session.AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo());
        session.FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings());
        session.InitializeCurrentFlipSettings(session.FlipSettings);
        var seen = new System.Collections.Generic.List<FlipSettings>();
        ProxyReqSyncCommand.WireStateSync(socket, session, () => seen.Add(session.FlipSettings.Value));

        var prevent = session.FlipSettings.ShouldPreventUpdate;
        prevent(new FlipSettings { Changer = socket.SessionInfo.ConnectionId }).Should().BeTrue("own writes are not applied again");
        prevent(new FlipSettings { Changer = "other" }).Should().BeFalse();
        seen.Clear(); // the own echo is announced to the sync, covered by ProxySyncSendsOwnWritesWhoseEchoIsNotAppliedAgain

        var changed = new FlipSettings { Changer = "other" };
        await session.FlipSettings.Update(changed);
        seen.Should().ContainSingle().Which.Should().BeSameAs(changed, "the state is sent after the value was assigned");
    }

    [Test]
    public void ProxySyncSendsOwnWritesWhoseEchoIsNotAppliedAgain()
    {
        var session = new NoSetupSession(socket);
        socket.SetLifecycle(session);
        session.AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo());
        session.FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings());
        session.InitializeCurrentFlipSettings(session.FlipSettings);
        var sentMinProfit = new System.Collections.Generic.List<long>();
        var sync = ProxyReqSyncCommand.WireStateSync(socket, session, () => sentMinProfit.Add(session.FlipSettings.Value.MinProfit));

        // /cofl set changes the current value in place and saves it with this connection as changer
        session.FlipSettings.Value.MinProfit = 5;
        session.FlipSettings.Value.Changer = socket.SessionInfo.ConnectionId;
        var echoPrevented = DeliverRedisEcho(session.FlipSettings, new FlipSettings { MinProfit = 5, Changer = socket.SessionInfo.ConnectionId });

        echoPrevented.Should().BeTrue("the redis subscription returns before OnChange/AfterChange, the own write is not applied again");
        sentMinProfit.Should().Equal([5L], "the proxy still has to receive the changed settings");

        sync.Stop();
        DeliverRedisEcho(session.FlipSettings, new FlipSettings { Changer = socket.SessionInfo.ConnectionId });
        sentMinProfit.Should().HaveCount(1, "nothing is sent after Stop");
    }

    /// <summary>The first step of the redis subscription callback, it stops when this returns true</summary>
    private static bool DeliverRedisEcho(SelfUpdatingValue<FlipSettings> value, FlipSettings echo)
        => value.ShouldPreventUpdate?.Invoke(echo) ?? false;

    [Test]
    public async Task ReplaceFlipSettingsDisposesTheValueWhenTheSessionEnded()
    {
        var recording = new HookedSocket();
        var session = new NoSetupSession(recording);
        recording.SetLifecycle(session);
        session.FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings());
        var current = session.FlipSettings;
        session.Dispose();
        var created = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings { BlackList = [], WhiteList = [] });

        await session.ReplaceFlipSettings(created);

        session.FlipSettings.Should().BeSameAs(current, "a released session keeps its values for the final cleanup");
        created.Value.Should().BeNull("nobody would release the copied settings subscription");
    }

    [Test]
    public async Task ReplaceFlipSettingsSwapsInTheValueOnALiveSession()
    {
        var recording = new HookedSocket();
        var session = new NoSetupSession(recording);
        recording.SetLifecycle(session);
        var previous = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings { BlackList = [], WhiteList = [] });
        session.FlipSettings = previous;
        var replaced = 0;
        session.ValuesReplaced += () => replaced++;
        var created = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings { BlackList = [], WhiteList = [] });

        await session.ReplaceFlipSettings(created);

        session.FlipSettings.Should().BeSameAs(created);
        created.Value.Should().NotBeNull();
        previous.Value.Should().BeNull("the replaced value is disposed");
        replaced.Should().Be(1);
    }

    [TestCase(null)]
    [TestCase("^(§r§eSell Offer|§r§6[Bazaar]|§r§cCancelled|§r§6Bazaar!|§r§eYou collected|§6[Auction]|§r§eBIN Auction started|§r§eYou §r§ccancelled|[Test]| - | + |Trade completed).*")]
    [TestCase("^custom messages$")]
    [TestCase(".*")]
    public void InventoryPrivacyNormalizesStoredRegexesExceptExplicitDebug(string storedRegex)
    {
        foreach (var noMessageBlocking in new[] { false, true })
        {
            var settings = PrivacySettings.Default;
            settings.ChatRegex = storedRegex;
            settings.ChatBlockRegex = "stale blocking regex";
            settings.NoMessageBlocking = noMessageBlocking;
            settings.CollectChat = false;
            settings.DisableTradeStoring = true;

            InventoryModSession.NormalizePrivacySettings(settings, keepDebugRegex: true);

            settings.ChatRegex.Should().Be(storedRegex == ".*" ? ".*" : PrivacySettings.DefaultChatRegex);
            settings.ChatBlockRegex.Should().Be(noMessageBlocking ? "$^" : PrivacySettings.DefaultChatBlockRegex);
            settings.CollectChat.Should().BeFalse();
            settings.DisableTradeStoring.Should().BeTrue();
            settings.NoMessageBlocking.Should().Be(noMessageBlocking);
        }
    }

    [Test]
    public void InventoryPrivacyResetsAStoredDebugRegexOnLogin()
    {
        var settings = PrivacySettings.Default;
        settings.ChatRegex = ".*";

        InventoryModSession.NormalizePrivacySettings(settings, keepDebugRegex: false);

        settings.ChatRegex.Should().Be(PrivacySettings.DefaultChatRegex);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InventorySessionNormalizesStoredPrivacyWithoutWriting(bool noMessageBlocking)
    {
        var stored = PrivacySettings.Default;
        stored.ChatRegex = ".*"; // a debug regex saved in an earlier session is the strongest case: login must still reset it
        stored.ChatBlockRegex = "stale blocking regex";
        stored.NoMessageBlocking = noMessageBlocking;
        stored.CollectChat = false;
        stored.DisableTradeStoring = true;
        var api = new Mock<ISettingsApi>();
        api.Setup(a => a.GetSettingWithHttpInfoAsync("7", "privacySettings", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Settings.Client.Client.ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject(stored)));
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object));
        var recording = new HookedSocket();
        using var session = new InventoryModSession(recording);
        recording.SetLifecycle(session);

        await session.SubToPrivacySettings("7");

        session.PrivacySettings.Value.ChatRegex.Should().Be(PrivacySettings.DefaultChatRegex);
        var sent = JsonConvert.DeserializeObject<PrivacySettings>(recording.Sent[^1].data);
        sent.ChatRegex.Should().Be(PrivacySettings.DefaultChatRegex);
        sent.ChatBlockRegex.Should().Be(noMessageBlocking ? "$^" : PrivacySettings.DefaultChatBlockRegex);
        sent.CollectChat.Should().BeFalse();
        sent.DisableTradeStoring.Should().BeTrue();
        sent.NoMessageBlocking.Should().Be(noMessageBlocking);
        api.Verify(a => a.UpdateSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task InventorySessionDisposedWhilePrivacySettingsLoadKeepsNone()
    {
        var gate = new TaskCompletionSource<Settings.Client.Client.ApiResponse<string>>();
        var api = new Mock<ISettingsApi>();
        api.Setup(a => a.GetSettingWithHttpInfoAsync("7", "privacySettings", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object));
        var recording = new HookedSocket();
        var session = new InventoryModSession(recording);
        recording.SetLifecycle(session);

        var load = session.SubToPrivacySettings("7");
        session.Dispose();
        gate.SetResult(new(HttpStatusCode.OK, JsonConvert.SerializeObject(new PrivacySettings())));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        session.PrivacySettings.Should().BeNull("settings created for a dead session are disposed, not stored");
    }

    [Test]
    public async Task LoginAdoptDisposesTheValueWhenTheSessionEnded()
    {
        var session = new NoSetupSession(socket);
        socket.SetLifecycle(session);
        var loginSocket = new Mock<IMinecraftSocket>();
        loginSocket.SetupGet(s => s.sessionLifesycle).Returns(session);
        var created = SelfUpdatingValue<string>.CreateNoUpdate("5");
        session.Dispose();

        await ModLoginExtensions.AdoptLogin(loginSocket.Object, created);

        created.Value.Should().BeNull("the created value is released, nobody else holds it");
        session.UserId.Should().NotBeSameAs(created);
    }

    [Test]
    public void ProxySyncStateIsNotSentForAReleasedSession()
    {
        var recording = new HookedSocket();
        var closed = false;
        recording.Closed = () => closed;
        var session = new NoSetupSession(recording);
        recording.SetLifecycle(session);
        SetSyncedValues(session);

        ProxyReqSyncCommand.SendState(recording);
        recording.Sent.Should().ContainSingle(r => r.type == "proxySync");

        closed = true;
        ProxyReqSyncCommand.SendState(recording);
        recording.Sent.Should().HaveCount(1, "a closing connection is not synced anymore");

        closed = false;
        session.Dispose();
        SetSyncedValues(session); // a setup step finishing late may still store values
        ProxyReqSyncCommand.SendState(recording);
        recording.Sent.Should().HaveCount(1, "a disposed session is not synced anymore");
    }

    private static void SetSyncedValues(ModSessionLifesycle session)
    {
        session.AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo());
        session.FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings());
    }

    [Test]
    public async Task OverlappingSetupsHookTheAccountInfoOnlyOnce()
    {
        var gates = new[] { new TaskCompletionSource<Settings.Client.Client.ApiResponse<string>>(),
            new TaskCompletionSource<Settings.Client.Client.ApiResponse<string>>() };
        var api = new Mock<ISettingsApi>();
        api.SetupSequence(a => a.GetSettingWithHttpInfoAsync("7", "flipSettings", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(gates[0].Task).Returns(gates[1].Task);
        api.Setup(a => a.GetSettingWithHttpInfoAsync("7", It.Is<string>(k => k != "flipSettings"), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Settings.Client.Client.ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject(new AccountInfo { UserId = "7" })));
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object));
        var session = new ProbeSession(socket);
        socket.SetLifecycle(session);
        session.TierManager = Mock.Of<IAccountTierManager>();
        var body = JsonConvert.SerializeObject(new FlipSettings { BlackList = [], WhiteList = [] });

        var first = session.Subscribe("7");
        var second = session.Subscribe("7");
        gates[0].SetResult(new(HttpStatusCode.OK, body));
        gates[1].SetResult(new(HttpStatusCode.OK, body));
        await Task.WhenAny(Task.WhenAll(first, second), Task.Delay(3000));
        var hooked = HandlerCount(session.AccountInfo);
        session.Dispose();

        hooked.Should().Be(1, "each run hooks the instance it created");
    }

    private static int HandlerCount(object value)
    {
        var field = value.GetType().GetField("OnChange", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (field.GetValue(value) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    [Test]
    public async Task LoginPromptDisposesTheValueWhenTheSessionEndedMeanwhile()
    {
        var gate = new TaskCompletionSource<Settings.Client.Client.ApiResponse<string>>();
        var api = new Mock<ISettingsApi>();
        api.Setup(a => a.GetSettingWithHttpInfoAsync(It.IsAny<string>(), "userId", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object));
        var session = new NoSetupSession(socket);
        socket.SetLifecycle(session);
        session.UserId = SelfUpdatingValue<string>.CreateNoUpdate((string)null!);
        var loginSocket = new Mock<IMinecraftSocket>();
        loginSocket.SetupGet(s => s.SessionInfo).Returns(socket.SessionInfo);
        loginSocket.SetupGet(s => s.sessionLifesycle).Returns(session);

        var prompt = loginSocket.Object.SendLoginPrompt();
        session.Dispose();
        gate.SetResult(new(HttpStatusCode.OK, JsonConvert.SerializeObject("5")));
        await prompt.WaitAsync(TimeSpan.FromSeconds(5));

        session.UserId?.Value.Should().BeNull("a value created for a dead session must not be stored");
    }

    [Test]
    public async Task LoginPromptWithoutLifecycleDoesNotThrow()
    {
        var api = new Mock<ISettingsApi>();
        api.Setup(a => a.GetSettingWithHttpInfoAsync(It.IsAny<string>(), "userId", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Settings.Client.Client.ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject("5")));
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object));
        var loginSocket = new Mock<IMinecraftSocket>();
        loginSocket.SetupGet(s => s.SessionInfo).Returns(socket.SessionInfo);
        loginSocket.SetupGet(s => s.sessionLifesycle).Returns((ModSessionLifesycle)null!);

        var prompt = () => loginSocket.Object.SendLoginPrompt();

        await prompt.Should().NotThrowAsync();
    }

    [Test]
    public void TryAdoptSwapsInTheCreatedValue()
    {
        var session = new NoSetupSession(socket);
        socket.SetLifecycle(session);
        var old = SelfUpdatingValue<string>.CreateNoUpdate("old");
        var field = old;
        var created = SelfUpdatingValue<string>.CreateNoUpdate("new");

        session.TryAdopt(ref field, created).Should().BeTrue();

        field.Should().BeSameAs(created);
        created.Value.Should().Be("new");
        old.Value.Should().BeNull("the replaced value is disposed");
    }

    [Test]
    public void TryAdoptDisposesTheValueWhenReleasedBeforeTheSwap()
    {
        var closing = new HookedSocket { Closed = () => true };
        var session = new NoSetupSession(closing);
        closing.SetLifecycle(session);
        var old = SelfUpdatingValue<string>.CreateNoUpdate("old");
        var field = old;
        var created = SelfUpdatingValue<string>.CreateNoUpdate("new");

        session.TryAdopt(ref field, created).Should().BeFalse();

        field.Should().BeSameAs(old, "a released session keeps its values for the final cleanup");
        old.Value.Should().Be("old");
        created.Value.Should().BeNull("nobody would release the created value");
    }

    [Test]
    public void TryAdoptDisposesTheValueWhenDisposedDuringTheSwap()
    {
        var hooked = new HookedSocket();
        var session = new NoSetupSession(hooked);
        hooked.SetLifecycle(session);
        // IsReleased reads the disposed flag before the socket, disposing here lands between the check and the swap
        var disposedOnce = false;
        hooked.Closed = () =>
        {
            if (!disposedOnce)
            {
                disposedOnce = true;
                session.Dispose();
            }
            return false;
        };
        var field = SelfUpdatingValue<string>.CreateNoUpdate("old");
        var created = SelfUpdatingValue<string>.CreateNoUpdate("new");

        session.TryAdopt(ref field, created).Should().BeFalse();

        disposedOnce.Should().BeTrue();
        created.Value.Should().BeNull("ReleaseResources ran before the swap and missed the created value");
    }

    private class HookedSocket : TestSocket
    {
        public Func<bool> Closed = () => false;
        public System.Collections.Generic.List<Response> Sent = new();
        public override bool IsClosed => Closed();
        public override void Send(Response response) => Sent.Add(response);
    }

    private class ExposedInventorySession : InventoryModSession
    {
        public ExposedInventorySession(MinecraftSocket socket) : base(socket) { }
        public Task Subscribe(string userId) => SubToSettings(userId);
    }

    private class ProbeSession : ModSessionLifesycle
    {
        public ProbeSession(MinecraftSocket socket) : base(socket) { }
        public Task Subscribe(string userId) => SubToSettings(userId);
        protected override void UpdateSettings(FlipSettings settings) { }
    }

    private class NoSetupSession : ModSessionLifesycle
    {
        public NoSetupSession(MinecraftSocket socket) : base(socket) { }
        protected override Task SubToSettings(string userId) => Task.CompletedTask;
        protected override void UpdateSettings(FlipSettings settings) { }
    }
}
