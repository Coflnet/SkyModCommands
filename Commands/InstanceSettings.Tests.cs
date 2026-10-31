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

    private static InstanceSocket CreateSocket(FlipSettings stored)
    {
        var service = new Mock<IInstanceSettingsService>();
        service.Setup(s => s.Get(SessionId)).ReturnsAsync(stored);
        var socket = new InstanceSocket(service.Object);
        socket.SessionInfo.SessionId = SessionId;
        socket.SetLifecycle(new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate((string)null)
        });
        return socket;
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
