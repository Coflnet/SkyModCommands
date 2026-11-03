using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[NonParallelizable]
public class SetCommandTests
{
    [Test]
    public async Task UserWithoutLoginGetsLoginHintFromPrivacySetting()
    {
        var wasDev = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        try
        {
            var option = SetCommand.Options.First(o => o.StartsWith("privacy", System.StringComparison.OrdinalIgnoreCase));
            var socket = new CapturingSocket();
            DiHandler.OverrideService<SettingsService, SettingsService>(new SettingsService(
                Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, Mock.Of<ISettingsApi>()));

            await new SetCommand().Execute(socket, JsonConvert.SerializeObject(option + " true"));

            Assert.That(socket.Sent.Any(m => m.Contains("/cofl verify")), Is.True, string.Join("|", socket.Sent));
        }
        finally
        {
            MinecraftSocket.IsDevMode = wasDev;
        }
    }

    [Test]
    public async Task UserWithoutLoginIsOfferedToKeepSettingsForInstance()
    {
        var wasDev = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
        try
        {
            var socket = new CapturingSocket();
            socket.SetLifecycle(new ModSessionLifesycle(socket)
            {
                UserId = SelfUpdatingValue<string>.CreateNoUpdate((string)null),
                FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(ModSessionLifesycle.DefaultSettings)
            });

            await new SetCommand().Execute(socket, JsonConvert.SerializeObject("minProfit 200000"));

            Assert.That(socket.Settings.MinProfit, Is.EqualTo(200000), "setting is applied to the session");
            Assert.That(socket.Clickable.Any(c => c.Contains("/cofl instancesettings enable")), Is.True,
                "signed-out user gets a clickable opt-in to keep settings for the mod instance, got: " + string.Join("|", socket.Sent));
        }
        finally
        {
            MinecraftSocket.IsDevMode = wasDev;
        }
    }

    private sealed class CapturingSocket : MinecraftSocket
    {
        public List<string> Sent { get; } = new();
        public List<string> Clickable { get; } = new();

        public void SetLifecycle(ModSessionLifesycle lifecycle) => sessionLifesycle = lifecycle;

        public override T GetService<T>()
        {
            try
            {
                return Mock.Of<T>();
            }
            catch (System.Exception)
            {
                return null; // services with constructor arguments are not needed by these tests
            }
        }

        public override bool SendMessage(params ChatPart[] parts)
        {
            Sent.AddRange(parts.Select(p => p.text));
            Clickable.AddRange(parts.Where(p => p.onClick != null).Select(p => p.onClick));
            return true;
        }
    }
}
