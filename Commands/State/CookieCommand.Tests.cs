using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class CookieCommandTests
{
    [Test]
    public async Task ActiveCookieShowsRemainingTime()
    {
        var messages = await Run(DateTime.UtcNow.AddHours(5));
        Assert.That(messages, Has.Some.Contains("Your cookie expires in"));
    }

    [Test]
    public async Task ExpiredCookieIsReportedInactive()
    {
        var messages = await Run(DateTime.UtcNow.AddHours(-5));
        Assert.That(messages, Has.Some.Contains("don't have an active cookie"));
    }

    [Test]
    public async Task UnknownCookieAsksToOpenMenu()
    {
        var messages = await Run(default);
        Assert.That(messages, Has.Some.Contains("open the SkyBlock menu"));
    }

    private static async Task<List<string>> Run(DateTime expires)
    {
        var api = new Mock<IPlayerStateApi>();
        api.Setup(a => a.PlayerStatePlayerIdExtractedGetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtractedInfo { BoosterCookieExpires = expires });
        var socket = new CaptureSocket(api.Object);
        await new CookieCommand().Execute(socket, "\"\"");
        return socket.Messages;
    }

    private class CaptureSocket(IPlayerStateApi api) : MinecraftSocket
    {
        public List<string> Messages { get; } = new();
        public override T GetService<T>() => api as T ?? Mock.Of<T>();
        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
        {
            Messages.AddRange(creation(new SocketDialogBuilder(this)).Build().Select(p => p.text));
        }
    }
}
