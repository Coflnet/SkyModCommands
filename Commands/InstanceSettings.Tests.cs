using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
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

    private static InstanceSocket CreateSocket(FlipSettings stored, Mock<IInstanceSettingsService> service = null)
    {
        service ??= new Mock<IInstanceSettingsService>();
        service.Setup(s => s.Get(SessionId)).ReturnsAsync(stored);
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

        public InstanceSocket(IInstanceSettingsService instanceSettings)
        {
            this.instanceSettings = instanceSettings;
            ConSpan = new Activity("test-connection");
        }

        public void SetLifecycle(ModSessionLifesycle lifecycle) => sessionLifesycle = lifecycle;

        public override Activity CreateActivity(string name, Activity parent = null) => null;

        public override T GetService<T>()
        {
            if (typeof(T) == typeof(IInstanceSettingsService))
                return (T)instanceSettings;
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
