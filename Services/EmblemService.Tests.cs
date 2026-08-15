using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class EmblemServiceTests
{
    [TestCase("384a029294fc445e863f2c42fe9709cb", true, true)]
    [TestCase("384a029294fc445e863f2c42fe9709cb", false, false)]
    [TestCase("00000000000000000000000000000000", true, false)]
    public async Task ModeratorEmblemIsOnlyAvailableToModerators(string minecraftUuid, bool verified, bool expected)
    {
        var socket = new Mock<MinecraftSocket>();
        socket.Object.SessionInfo.McUuid = minecraftUuid;
        socket.Object.SessionInfo.VerifiedMc = verified;
        socket.Setup(s => s.GetService<ModeratorService>()).Returns(new ModeratorService());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string>("PLAYERSTATE_BASE_URL", "http://player-state") })
            .Build();
        var service = new EmblemService(
            new HttpClient(new EmptyAchievementsHandler()),
            config,
            NullLogger<EmblemService>.Instance);

        var unlocked = await service.GetUnlockedForSocket(socket.Object);

        Assert.That(unlocked.Contains(Emblems.Moderator), Is.EqualTo(expected));
    }

    private sealed class EmptyAchievementsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        }
    }
}
