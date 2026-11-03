using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Newtonsoft.Json;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[NonParallelizable]
public class InstanceSettingsTests
{
    private const string SessionId = "instance-session";

    [Test]
    public async Task SignedOutReconnectAppliesStoredSettingsAndKeybinds()
    {
        var stored = ModSessionLifesycle.DefaultSettings;
        stored.MinProfit = 123456;
        stored.ModSettings.Hotkeys["key.keyboard.k"] = "/cofl flip";
        var socket = CreateSocket(stored);

        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        Assert.That(socket.sessionLifesycle.SavesSettingsForInstance, Is.True);
        Assert.That(socket.Settings.MinProfit, Is.EqualTo(123456), "stored settings are used for the session");
        Assert.That(socket.SentResponses.Any(r => r.type == "registerKeybind" && r.data.Contains("key.keyboard.k")), Is.True,
            "stored hotkeys are registered on reconnect, sent: " + string.Join("|", socket.SentResponses.Select(r => r.type)));
        Assert.That(socket.Errors, Is.Empty);
    }

    [Test]
    public async Task SignedInAtConnectIsOfferedCopyOfStoredSettings()
    {
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings);
        socket.sessionLifesycle.UserId = SelfUpdatingValue<string>.CreateNoUpdate("5");

        await socket.sessionLifesycle.OfferInstanceSettingsCopy();

        Assert.That(socket.sessionLifesycle.SavesSettingsForInstance, Is.True);
        Assert.That(socket.Clickable.Any(c => c.Contains("/cofl instancesettings copy")), Is.True,
            "copy offer is sent, got: " + string.Join("|", socket.Sent));
    }

    [Test]
    public async Task FlipNeverIsKeptForInstance()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        await new FlipCommand().Execute(socket, "\"never\"");

        service.Verify(s => s.Save(SessionId, It.Is<FlipSettings>(f => f.DisableFlips)), Times.Once,
            "disabling flips of an opted-in signed-out session is stored for the mod instance");
    }

    [Test]
    public async Task KeybindIsKeptForInstance()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        await new KeyBindCommand().Execute(socket, "\"add k /cofl flip\"");

        service.Verify(s => s.Save(SessionId, It.Is<FlipSettings>(f => f.ModSettings.Hotkeys.ContainsKey("k"))), Times.Once,
            "a hotkey of an opted-in signed-out session is stored for the mod instance, errors: " + string.Join("|", socket.Errors));
    }

    [Test]
    public async Task ChatToggleIsKeptForInstance()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        var chatWasOn = socket.Settings.ModSettings.Chat;

        try
        {
            await new ChatCommand().Execute(socket, "\"toggle\"");
        }
        catch (InvalidOperationException)
        {
            // the plain text confirmation after saving needs an open websocket
        }

        service.Verify(s => s.Save(SessionId, It.Is<FlipSettings>(f => f.ModSettings.Chat != chatWasOn)), Times.Once,
            "toggling the chat popups of an opted-in signed-out session is stored for the mod instance");
    }

    [Test]
    public async Task SignedInChangeIsNotKeptForInstance()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        socket.sessionLifesycle.UserId = SelfUpdatingValue<string>.CreateNoUpdate("5");

        await new FlipCommand().Execute(socket, "\"never\"");

        service.Verify(s => s.Save(It.IsAny<string>(), It.IsAny<FlipSettings>()), Times.Never,
            "a signed-in change is saved with the account only, not also for the mod instance");
    }

    [Test]
    public async Task CopyReplacesAccountSettingsWithoutKeepingThemForInstance()
    {
        var stored = ModSessionLifesycle.DefaultSettings;
        stored.MinProfit = 777;
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(stored, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        socket.sessionLifesycle.UserId = SelfUpdatingValue<string>.CreateNoUpdate("5");

        await new InstanceSettingsCommand().Execute(socket, "\"copy\"");

        Assert.That(socket.Settings.MinProfit, Is.EqualTo(777), "stored settings replace the account settings");
        service.Verify(s => s.Save(It.IsAny<string>(), It.IsAny<FlipSettings>()), Times.Never,
            "the copy is saved with the account only, errors: " + string.Join("|", socket.Errors));
        service.Verify(s => s.Delete(SessionId), Times.Once);
    }

    [Test]
    public async Task SignedOutLoreSetIsKeptForInstanceWithoutLogin()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");

        service.Verify(s => s.SaveDescription(SessionId, It.Is<DescriptionSetting>(d => d.DisableInfoIn.Contains("bazaar"))), Times.Once,
            "errors: " + string.Join("|", socket.Errors) + " sent: " + string.Join("|", socket.Sent));
        service.Verify(s => s.Save(It.IsAny<string>(), It.IsAny<FlipSettings>()), Times.Never, "flip settings did not change");
        Assert.That(socket.Sent.Any(m => m.Contains("We could not determine")), Is.False, string.Join("|", socket.Sent));
        Assert.That(socket.Sent.Any(m => m.Contains("Set ")), Is.True, "user gets the usual confirmation");
    }

    [Test]
    public async Task SignedOutLoreSetWithoutOptInShowsDialogAndSavesNothing()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(null, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");

        Assert.That(socket.Clickable.Any(c => c.Contains("/cofl instancesettings enable")), Is.True, string.Join("|", socket.Sent));
        service.Verify(s => s.SaveDescription(It.IsAny<string>(), It.IsAny<DescriptionSetting>()), Times.Never);
        service.Verify(s => s.Save(It.IsAny<string>(), It.IsAny<FlipSettings>()), Times.Never);
    }

    [Test]
    public async Task OptingInAppliesThePendingLoreChange()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(null, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");

        await new InstanceSettingsCommand().Execute(socket, "\"enable\"");

        service.Verify(s => s.SaveDescription(SessionId, It.Is<DescriptionSetting>(d => d.DisableInfoIn.Contains("bazaar"))), Times.Once,
            "errors: " + string.Join("|", socket.Errors));
    }

    [Test]
    public async Task SignedOutPrivacySetExplainsAccountIsNeeded()
    {
        var wasDev = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        try
        {
            var socket = CreateSocket(ModSessionLifesycle.DefaultSettings);
            await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

            await new SetCommand().Execute(socket, "\"privacyDisableTradeStoring true\"");

            Assert.That(socket.Sent.Any(m => m.Contains("/cofl login")), Is.True, string.Join("|", socket.Sent));
            Assert.That(socket.Sent.Any(m => m.Contains("We could not determine")), Is.False);
        }
        finally
        {
            MinecraftSocket.IsDevMode = wasDev;
        }
    }

    [Test]
    public async Task SignedOutSettingsPageShowsStoredLoreValues()
    {
        var wasDev = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        try
        {
            var service = new Mock<IInstanceSettingsService>();
            service.Setup(s => s.GetDescription(SessionId)).ReturnsAsync(new DescriptionSetting { DisableInfoIn = new HashSet<string> { "stored-value" } });
            var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
            await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
            var visible = SetCommand.updater.ModOptions.Where(o => !o.Value.Hide).Select(o => o.Key).ToList();
            var page = visible.IndexOf("loreDisableInfoIn") / 10 + 1;

            await new SetCommand().Execute(socket, JsonConvert.SerializeObject(page.ToString()));

            Assert.That(socket.Sent.Any(m => m.Contains("loreDisableInfoIn") && m.Contains("stored-value")), Is.True, string.Join("|", socket.Sent));
            Assert.That(socket.Sent.Any(m => m.Contains("an error occured")), Is.False);
        }
        finally
        {
            MinecraftSocket.IsDevMode = wasDev;
        }
    }

    [Test]
    public async Task FailedReadDuringLoreSetDoesNotOverwriteStoredSettings()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        service.Setup(s => s.GetDescriptionStrict(SessionId)).ThrowsAsync(new InvalidOperationException("db down"));

        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");

        service.Verify(s => s.SaveDescription(It.IsAny<string>(), It.IsAny<DescriptionSetting>()), Times.Never);
        Assert.That(socket.Sent.Any(m => m.Contains("could not load your settings")), Is.True, string.Join("|", socket.Sent));
    }

    [Test]
    public async Task ConcurrentLoreSetsAreBothKept()
    {
        var stored = JsonConvert.SerializeObject(DescriptionSetting.Default);
        var service = new Mock<IInstanceSettingsService>();
        service.Setup(s => s.GetDescriptionStrict(SessionId)).Returns(async () =>
        {
            var snapshot = stored;
            await Task.Delay(50);
            return JsonConvert.DeserializeObject<DescriptionSetting>(snapshot);
        });
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        service.Setup(s => s.SaveDescription(SessionId, It.IsAny<DescriptionSetting>()))
            .Returns((string _, DescriptionSetting d) => { stored = JsonConvert.SerializeObject(d); return Task.FromResult(true); });
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        await Task.WhenAll(
            new SetCommand().Execute(socket, "\"loreDisableInfoIn first\""),
            new SetCommand().Execute(socket, "\"loreDisableInfoIn second\""));

        var saved = JsonConvert.DeserializeObject<DescriptionSetting>(stored);
        Assert.That(saved.DisableInfoIn, Is.SupersetOf(new[] { "first", "second" }), string.Join("|", socket.Errors));
    }

    [Test]
    public async Task InvalidLoreValueIsRejectedBeforeOptIn()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(null, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        await new SetCommand().Execute(socket, "\"loreLowballMedUndercut abc\"");
        await new InstanceSettingsCommand().Execute(socket, "\"enable\"");

        service.Verify(s => s.SaveDescription(It.IsAny<string>(), It.IsAny<DescriptionSetting>()), Times.Never);
        Assert.That(socket.Sent.Any(m => m.Contains("number")), Is.True, "the parse error is shown right away: " + string.Join("|", socket.Sent));
        Assert.That(socket.Sent.Any(m => m.Contains("Applied")), Is.False);
    }

    [Test]
    public async Task LoreOptInDialogSaysNothingIsActiveYet()
    {
        var socket = CreateSocket(null);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");

        Assert.That(socket.Sent.Any(m => m.Contains("nothing is active yet")), Is.True, string.Join("|", socket.Sent));
    }

    [Test]
    public void AccountInfoAloneCountsAsSignedIn()
    {
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings);

        Assert.That(socket.sessionLifesycle.IsSignedOut, Is.True);
        socket.sessionLifesycle.AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { UserId = "5" });

        Assert.That(socket.sessionLifesycle.IsSignedOut, Is.False, "socket.UserId falls back to the account info, so must this");
    }

    [Test]
    public async Task SignedOutLoreCommandChangesInstanceSettings()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        try
        {
            await new LoreCommand().Execute(socket, "\"disable\"");
        }
        catch (InvalidOperationException)
        {
            // the plain text confirmation after saving needs an open websocket
        }

        service.Verify(s => s.SaveDescription(SessionId, It.Is<DescriptionSetting>(d => d.Disabled)), Times.Once,
            "errors: " + string.Join("|", socket.Errors) + " sent: " + string.Join("|", socket.Sent));
    }

    [Test]
    public async Task CopyWritesStoredDescriptionToAccount()
    {
        var wasDev = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        try
        {
            var api = new Mock<ISettingsApi>();
            var service = new Mock<IInstanceSettingsService>();
            service.Setup(s => s.GetDescriptionStrict(SessionId)).ReturnsAsync(new DescriptionSetting { DisableInfoIn = new HashSet<string> { "bazaar" } });
            var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
            socket.SettingsService = new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object);
            await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
            socket.sessionLifesycle.UserId = SelfUpdatingValue<string>.CreateNoUpdate("5");

            await new InstanceSettingsCommand().Execute(socket, "\"copy\"");

            api.Verify(a => a.UpdateSettingAsync("5", "description", It.Is<string>(v => v.Contains("bazaar")), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once,
                "errors: " + string.Join("|", socket.Errors));
        }
        finally
        {
            MinecraftSocket.IsDevMode = wasDev;
        }
    }

    [Test]
    public async Task LoginDuringTheLoadKeepsTheAccountSettings()
    {
        var gate = new TaskCompletionSource<FlipSettings>();
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        service.Setup(s => s.Get(SessionId)).Returns(gate.Task);

        var setup = socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        // the login landed while the instance settings loaded and installed the account settings
        var account = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings { MinProfit = 999 });
        socket.sessionLifesycle.FlipSettings = account;
        gate.SetResult(ModSessionLifesycle.DefaultSettings);
        await setup;

        Assert.That(socket.sessionLifesycle.FlipSettings, Is.SameAs(account), "the signed-out settings must not overwrite the account settings");
        Assert.That(account.Value, Is.Not.Null, "the account settings stay alive");
    }

    [Test]
    public async Task SignedOutSettingsAnnounceTheReplacement()
    {
        var socket = CreateSocket(null);
        var replaced = 0;
        socket.sessionLifesycle.ValuesReplaced += () => replaced++;

        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        Assert.That(replaced, Is.EqualTo(1), "listeners like the proxy sync move to the installed settings");
        Assert.That(socket.Settings, Is.Not.Null);
    }

    [Test]
    public async Task FailedEnableLeavesNoPhantomOptIn()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(null, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        service.Setup(s => s.Save(SessionId, It.IsAny<FlipSettings>())).ThrowsAsync(new InvalidOperationException("db down"));

        Assert.ThrowsAsync<InvalidOperationException>(() => socket.sessionLifesycle.EnableInstanceSettings());
        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");

        Assert.That(socket.sessionLifesycle.SavesSettingsForInstance, Is.False);
        Assert.That(socket.Clickable.Any(c => c.Contains("/cofl instancesettings enable")), Is.True, "opt-in dialog is shown again: " + string.Join("|", socket.Sent));
        Assert.That(socket.Sent.Any(m => m.Contains("Set ")), Is.False, "no success message for an unsaved change");
        service.Verify(s => s.SaveDescription(It.IsAny<string>(), It.IsAny<DescriptionSetting>()), Times.Never);
    }

    [Test]
    public async Task LoreSetWithoutStoredRowIsNotReportedAsSaved()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        service.Setup(s => s.SaveDescription(SessionId, It.IsAny<DescriptionSetting>())).ReturnsAsync(false);

        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");

        Assert.That(socket.Sent.Any(m => m.Contains("Set ")), Is.False, "nothing was stored: " + string.Join("|", socket.Sent));
        Assert.That(socket.Clickable.Any(c => c.Contains("/cofl instancesettings enable")), Is.True, "user is asked to opt in again");
        Assert.That(socket.sessionLifesycle.SavesSettingsForInstance, Is.False, "the opt-in is void without a row");
    }

    [Test]
    public async Task ChangeLostWithTheRowIsAppliedAfterOptingInAgain()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        service.Setup(s => s.SaveDescription(SessionId, It.IsAny<DescriptionSetting>())).ReturnsAsync(false);
        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");
        service.Setup(s => s.SaveDescription(SessionId, It.IsAny<DescriptionSetting>())).ReturnsAsync(true);

        await new InstanceSettingsCommand().Execute(socket, "\"enable\"");

        service.Verify(s => s.SaveDescription(SessionId, It.Is<DescriptionSetting>(d => d.DisableInfoIn.Contains("bazaar"))), Times.Exactly(2));
        Assert.That(socket.sessionLifesycle.SavesSettingsForInstance, Is.True);
    }

    [Test]
    public async Task OptInAdoptsStoredSettingsAfterFailedConnectRead()
    {
        var stored = ModSessionLifesycle.DefaultSettings;
        stored.MinProfit = 555;
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(null, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        service.Setup(s => s.GetStrict(SessionId)).ReturnsAsync(stored);

        await socket.sessionLifesycle.EnableInstanceSettings();

        service.Verify(s => s.Save(It.IsAny<string>(), It.IsAny<FlipSettings>()), Times.Never, "the stored flip settings must not be overwritten by defaults");
        Assert.That(socket.Settings.MinProfit, Is.EqualTo(555), "the stored settings are used for the session");
        Assert.That(socket.sessionLifesycle.SavesSettingsForInstance, Is.True);
    }

    [Test]
    public async Task OptInDoesNotOverwriteWhenTheStrictReadFails()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(null, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        service.Setup(s => s.GetStrict(SessionId)).ThrowsAsync(new InvalidOperationException("db down"));

        Assert.ThrowsAsync<InvalidOperationException>(() => socket.sessionLifesycle.EnableInstanceSettings());

        service.Verify(s => s.Save(It.IsAny<string>(), It.IsAny<FlipSettings>()), Times.Never);
        Assert.That(socket.sessionLifesycle.SavesSettingsForInstance, Is.False);
    }

    [Test]
    public async Task ConnectWithStoredSettingsRefreshesTheTtl()
    {
        var refreshed = new TaskCompletionSource();
        var service = new Mock<IInstanceSettingsService>();
        service.Setup(s => s.RefreshTtl(SessionId)).Returns(() => { refreshed.SetResult(); return Task.CompletedTask; });
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);

        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        Assert.That(await Task.WhenAny(refreshed.Task, Task.Delay(5000)), Is.SameAs(refreshed.Task), "settings in use stay alive");
    }

    [Test]
    public async Task FailingTtlRefreshDoesNotBreakTheConnect()
    {
        var service = new Mock<IInstanceSettingsService>();
        service.Setup(s => s.RefreshTtl(SessionId)).ThrowsAsync(new InvalidOperationException("db down"));
        var socket = CreateSocket(ModSessionLifesycle.DefaultSettings, service);

        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        Assert.That(socket.sessionLifesycle.SavesSettingsForInstance, Is.True);
    }

    [Test]
    public async Task ConnectWithoutStoredSettingsDoesNotRefreshTheTtl()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(null, service);

        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);
        await Task.Delay(50);

        service.Verify(s => s.RefreshTtl(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task SignedInLoreCommandNeverTouchesInstanceSettings()
    {
        using var devOff = new DevModeOff();
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSignedInSocket(service, out _);

        try
        {
            await new LoreCommand().Execute(socket, "\"disable\"");
        }
        catch (InvalidOperationException)
        {
            // the plain text confirmation after saving needs an open websocket
        }

        Assert.That(service.Invocations, Is.Empty, "signed-in users stay on account settings");
    }

    [Test]
    public async Task SignedInHighlightNeverTouchesInstanceSettings()
    {
        using var devOff = new DevModeOff();
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSignedInSocket(service, out var saved);
        var location = JsonConvert.SerializeObject(new SearchCommand.ItemLocation { Chestname = "chest" });

        await new HighlightItemCommand().Execute(socket, JsonConvert.SerializeObject(location));

        Assert.That(service.Invocations, Is.Empty);
        Assert.That(saved.Any(b => b.Contains("chest")), Is.True, "saved to the account: " + string.Join("|", socket.Errors));
    }

    [Test]
    public async Task SignedInLoreSetSavesToAccountOnly()
    {
        using var devOff = new DevModeOff();
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSignedInSocket(service, out var saved);

        await new SetCommand().Execute(socket, "\"loreDisableInfoIn bazaar\"");

        Assert.That(saved.Any(b => b.Contains("bazaar")), Is.True, string.Join("|", socket.Errors) + string.Join("|", socket.Sent));
        Assert.That(service.Invocations, Is.Empty);
        Assert.That(socket.Sent.Any(m => m.Contains("Set ")), Is.True);
    }

    [Test]
    public void LoreCommandsWithoutSessionAskForLogin()
    {
        var socket = new InstanceSocket(Mock.Of<IInstanceSettingsService>());

        Assert.ThrowsAsync<NoLoginException>(() => new LoreCommand().Execute(socket, "\"disable\""));
        Assert.ThrowsAsync<NoLoginException>(() => new HighlightItemCommand().Execute(socket, "\"{}\""));
    }

    [Test]
    public async Task FoundModsStaysSilentForSignedOutUserWithoutOptIn()
    {
        var service = new Mock<IInstanceSettingsService>();
        var socket = CreateSocket(null, service);
        await socket.sessionLifesycle.SetupSignedOutSettings(SessionId);

        await new FoundModsCommand().Execute(socket, JsonConvert.SerializeObject(new FoundModsCommand.Response { FileNames = ["Firmament-3.9.0.jar"] }));

        Assert.That(socket.Clickable, Is.Empty, "no opt-in prompt out of the blue: " + string.Join("|", socket.Sent));
        service.Verify(s => s.SaveDescription(It.IsAny<string>(), It.IsAny<DescriptionSetting>()), Times.Never);
    }

    /// <summary>
    /// A signed-in socket whose account description writes are collected in <paramref name="saved"/>
    /// </summary>
    private static InstanceSocket CreateSignedInSocket(Mock<IInstanceSettingsService> service, out List<string> saved)
    {
        var bodies = new List<string>();
        saved = bodies;
        var api = new Mock<ISettingsApi>();
        api.Setup(a => a.GetSettingWithHttpInfoAsync("5", "description", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(() => new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(System.Net.HttpStatusCode.OK, JsonConvert.SerializeObject(DescriptionSetting.Default)));
        api.Setup(a => a.UpdateSettingAsync("5", "description", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback((string _, string _, string body, int _, System.Threading.CancellationToken _) => bodies.Add(body));
        var socket = new InstanceSocket(service.Object)
        {
            SettingsService = new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object)
        };
        socket.SessionInfo.SessionId = SessionId;
        socket.SetLifecycle(new QuietLifecycle(socket) { UserId = SelfUpdatingValue<string>.CreateNoUpdate("5") });
        return socket;
    }

    private sealed class DevModeOff : IDisposable
    {
        private readonly bool wasDev = MinecraftSocket.IsDevMode;
        public DevModeOff() => MinecraftSocket.IsDevMode = false;
        public void Dispose() => MinecraftSocket.IsDevMode = wasDev;
    }

    private static InstanceSocket CreateSocket(FlipSettings stored, Mock<IInstanceSettingsService> service = null)
    {
        service ??= new Mock<IInstanceSettingsService>();
        service.Setup(s => s.Get(SessionId)).ReturnsAsync(stored);
        service.Setup(s => s.SaveDescription(SessionId, It.IsAny<DescriptionSetting>())).ReturnsAsync(true);
        var socket = new InstanceSocket(service.Object);
        socket.SessionInfo.SessionId = SessionId;
        socket.SetLifecycle(new QuietLifecycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate((string)null)
        });
        return socket;
    }

    /// <summary>
    /// Skips applying updates, which writes to the not opened websocket
    /// </summary>
    private sealed class QuietLifecycle(MinecraftSocket socket) : ModSessionLifesycle(socket)
    {
        protected override void UpdateSettings(FlipSettings settings) { }
    }

    private sealed class InstanceSocket : MinecraftSocket
    {
        private readonly IInstanceSettingsService instanceSettings;
        private readonly FlipperService flipper = new(null, NullLogger<FlipperService>.Instance);
        public List<string> Sent { get; } = new();
        public List<string> Clickable { get; } = new();
        public List<Response> SentResponses { get; } = new();
        public List<string> Errors { get; } = new();

        public SettingsService SettingsService { get; set; }

        public InstanceSocket(IInstanceSettingsService instanceSettings)
        {
            this.instanceSettings = instanceSettings;
            ConSpan = new Activity("test-connection");
        }

        public void SetLifecycle(ModSessionLifesycle lifecycle) => sessionLifesycle = lifecycle;

        /// <summary>The base reads the websocket state, which throws without a real session</summary>
        public override bool IsClosed => false;

        public override Activity CreateActivity(string name, Activity parent = null) => null;

        public override T GetService<T>()
        {
            if (typeof(T) == typeof(IInstanceSettingsService))
                return (T)instanceSettings;
            if (typeof(T) == typeof(SettingsService) && SettingsService != null)
                return (T)(object)SettingsService;
            if (typeof(T) == typeof(FlipperService))
                return (T)(object)flipper;
            return Mock.Of<T>();
        }

        public override void Send(Response response) => SentResponses.Add(response);

        public override string Error(Exception exception, string message = null, string additionalLog = null)
        {
            Errors.Add($"{message}: {exception}");
            return message;
        }

        public override bool SendMessage(params ChatPart[] parts)
        {
            Sent.AddRange(parts.Select(p => p.text));
            Clickable.AddRange(parts.Where(p => p.onClick != null).Select(p => p.onClick));
            return true;
        }
    }
}
