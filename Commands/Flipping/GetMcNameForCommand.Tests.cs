using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.PlayerName;
using Coflnet.Sky.PlayerName.Client.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Tests;

public class GetMcNameForCommandTests
{
    private const string Uuid = "0123456789abcdef0123456789abcdef";

    private sealed class Mojang(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.RequestUri.ToString(), Is.EqualTo($"https://sessionserver.mojang.com/session/minecraft/profile/{Uuid}"));
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class HangingMojang : HttpMessageHandler
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Release.Task;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        }
    }

    [Test]
    public async Task AhOpenUsesStoredNameWithoutWaitingForMojang()
    {
        var names = new Mock<IPlayerNameApi>();
        names.Setup(n => n.PlayerNameNameUuidGetAsync(Uuid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("\"OldName\"");
        DiHandler.OverrideService<PlayerNameService, PlayerNameService>(
            new PlayerNameService(names.Object, NullLogger<PlayerNameService>.Instance));
        using var mojang = new HangingMojang();
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(mojang, false));
        var executed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var socket = new Mock<MinecraftSocket>();
        socket.Setup(s => s.GetService<IHttpClientFactory>()).Returns(clients.Object);
        socket.Setup(s => s.CreateActivity(It.IsAny<string>(), It.IsAny<System.Diagnostics.Activity>())).Returns(() => new System.Diagnostics.Activity("test"));
        socket.Setup(s => s.ExecuteCommand(It.IsAny<string>())).Callback<string>(c => executed.TrySetResult(c));

        _ = new AhOpenCommand().Execute(socket.Object, $"\"{Uuid}\"");
        var first = await Task.WhenAny(executed.Task, Task.Delay(2000));
        mojang.Release.TrySetResult();

        Assert.That(first, Is.SameAs(executed.Task), "ah open waited for the mojang name check");
        Assert.That(await executed.Task, Is.EqualTo("/ah OldName"));
    }

    [TestCase(HttpStatusCode.OK, "{\"id\":\"" + Uuid + "\",\"name\":\"NewName\"}", "NewName", TestName = "renamed player returns the current name")]
    [TestCase(HttpStatusCode.OK, "{\"id\":\"" + Uuid + "\",\"name\":\"oldname\"}", "OldName", TestName = "matching name keeps the stored name")]
    [TestCase(HttpStatusCode.TooManyRequests, "", "OldName", TestName = "mojang failure falls back to the stored name")]
    public async Task VerifiesStoredNameWithMojang(HttpStatusCode status, string body, string expected)
    {
        var names = new Mock<IPlayerNameApi>();
        names.Setup(n => n.PlayerNameNameUuidGetAsync(Uuid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("\"OldName\"");
        DiHandler.OverrideService<PlayerNameService, PlayerNameService>(
            new PlayerNameService(names.Object, NullLogger<PlayerNameService>.Instance));
        using var mojang = new Mojang(status, body);
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(mojang, false));
        var socket = new Mock<MinecraftSocket>();
        socket.Setup(s => s.GetService<IHttpClientFactory>()).Returns(clients.Object);

        var name = await GetMcNameForCommand.GetName(socket.Object, $"\"{Uuid}\"");

        Assert.That(name, Is.EqualTo(expected));
        Assert.That(mojang.Requests, Is.EqualTo(1));
    }
}
