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

    private sealed class CapturingSocket : MinecraftSocket
    {
        public List<string> Sent { get; } = new();

        public override bool SendMessage(params ChatPart[] parts)
        {
            Sent.AddRange(parts.Select(p => p.text));
            return true;
        }
    }
}
