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

namespace Coflnet.Sky.Commands.MC.Tasks;

public class StatsRefreshTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void Reset() => StatsRefresh.ResetThrottle();

    private static ExtractedInfo Info(DateTime at, bool withStats = true) => new()
    {
        PlayerStatsAt = at,
        PlayerStats = withStats ? new Dictionary<string, double> { ["Mining Speed"] = 100 } : null
    };

    [Test]
    public void DueWhenMissing()
    {
        Assert.That(StatsRefresh.IsDue(null, Now), Is.True);
        Assert.That(StatsRefresh.IsDue(Info(default, false), Now), Is.True);
        Assert.That(StatsRefresh.IsDue(Info(Now.AddHours(-1), false), Now), Is.True);
    }

    [Test]
    public void DueWhenOlderThanSevenDays()
        => Assert.That(StatsRefresh.IsDue(Info(Now.AddDays(-8)), Now), Is.True);

    [Test]
    public void NotDueWhenFresh()
        => Assert.That(StatsRefresh.IsDue(Info(Now.AddDays(-6)), Now), Is.False);

    [Test]
    public async Task OpensStatsOnceWithMessage()
    {
        var socket = new CaptureSocket(Api(Info(default, false)));

        await StatsRefresh.RefreshIfDue(socket, Now);

        Assert.That(socket.Executed, Is.EqualTo(new[] { "/stats" }));
        Assert.That(socket.Messages, Has.Some.Contains("Briefly opening your stats menu"));
    }

    [Test]
    public async Task ThrottlesEvenWhenStoredStatsStayStale()
    {
        var socket = new CaptureSocket(Api(Info(default, false)));

        await StatsRefresh.RefreshIfDue(socket, Now);
        await StatsRefresh.RefreshIfDue(socket, Now.AddDays(3));
        Assert.That(socket.Executed, Has.Count.EqualTo(1));

        await StatsRefresh.RefreshIfDue(socket, Now.AddDays(8));
        Assert.That(socket.Executed, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task FreshStatsDoNotOpenMenu()
    {
        var socket = new CaptureSocket(Api(Info(Now.AddDays(-1))));

        await StatsRefresh.RefreshIfDue(socket, Now);

        Assert.That(socket.Executed, Is.Empty);
    }

    [Test]
    public async Task LookupFailureIsSwallowedAndSkipsRefresh()
    {
        var api = new Mock<IPlayerStateApi>();
        api.Setup(a => a.PlayerStatePlayerIdExtractedGetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());
        var socket = new CaptureSocket(api.Object);

        Assert.DoesNotThrowAsync(() => StatsRefresh.RefreshIfDue(socket));

        Assert.That(socket.Executed, Is.Empty);
        Assert.That(socket.Messages, Is.Empty);
    }

    private static IPlayerStateApi Api(ExtractedInfo info)
    {
        var api = new Mock<IPlayerStateApi>();
        api.Setup(a => a.PlayerStatePlayerIdExtractedGetAsync("Tester", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(info);
        return api.Object;
    }

    private class CaptureSocket : MinecraftSocket
    {
        private readonly IPlayerStateApi api;
        public List<string> Messages { get; } = new();
        public List<string> Executed { get; } = new();

        public CaptureSocket(IPlayerStateApi api)
        {
            this.api = api;
            SessionInfo.McName = "Tester";
        }

        public override T GetService<T>() => api as T ?? Mock.Of<T>();
        public override void ExecuteCommand(string command) => Executed.Add(command);
        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
            => Messages.AddRange(creation(new SocketDialogBuilder(this)).Build().Select(p => p.text));
    }
}
