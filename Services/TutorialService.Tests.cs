using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services.Tests;

public class TutorialServiceTests
{
    private List<SelfUpdatingValue<HashSet<string>>> created;
    private TutorialService service;

    [SetUp]
    public void Setup()
    {
        created = new();
        service = new TutorialService(async _ =>
        {
            await Task.Delay(20);
            var value = SelfUpdatingValue<HashSet<string>>.CreateNoUpdate(new HashSet<string>());
            lock (created)
                created.Add(value);
            return value;
        });
    }

    [Test]
    public async Task ConcurrentFirstCallsCreateOneState()
    {
        var states = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.GetSolved(new object(), "1")));

        created.Should().HaveCount(1);
        states.Distinct().Should().HaveCount(1);
        states[0].SolvedTutorials.Value.Should().NotBeNull();
    }

    [Test]
    public async Task ReleaseDisposesStateAfterLastConnectionClosed()
    {
        var first = Mock.Of<IMinecraftSocket>();
        var second = Mock.Of<IMinecraftSocket>();
        var state = await service.GetSolved(first, "1");
        var sameState = await service.GetSolved(second, "1");
        sameState.Should().BeSameAs(state);

        service.Release(first);
        await Task.Delay(50);
        state.SolvedTutorials.Value.Should().NotBeNull("the second connection still uses it");

        service.Release(second);
        await WaitFor(() => state.SolvedTutorials.Value == null);
        state.SolvedTutorials.Value.Should().BeNull();
    }

    [Test]
    public async Task StateIsRecreatedAfterRelease()
    {
        var socket = Mock.Of<IMinecraftSocket>();
        var state = await service.GetSolved(socket, "1");
        service.Release(socket);

        var next = await service.GetSolved(Mock.Of<IMinecraftSocket>(), "1");

        next.Should().NotBeSameAs(state);
        next.SolvedTutorials.Value.Should().NotBeNull();
    }

    [Test]
    public void ReleaseOfUnknownSocketDoesNothing()
    {
        service.Invoking(s => s.Release(Mock.Of<IMinecraftSocket>())).Should().NotThrow();
        service.Invoking(s => s.Release(null)).Should().NotThrow();
    }

    private static async Task WaitFor(System.Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
    }

    [Test]
    public async Task GetSolvedAfterReleaseDoesNotRegisterTheConnection()
    {
        var socket = Mock.Of<IMinecraftSocket>();
        service.Release(socket);

        var state = await service.GetSolved(socket, "1");

        state.Should().BeNull();
        created.Should().BeEmpty("nobody would release the state again");
    }

    [Test]
    public async Task GetSolvedForClosedConnectionIsSkipped()
    {
        var socket = Mock.Of<IMinecraftSocket>(s => s.IsClosed == true);

        var state = await service.GetSolved(socket, "1");

        state.Should().BeNull();
        created.Should().BeEmpty();
    }

    [Test]
    public async Task ReleaseWhileStateIsLoadingDisposesTheLateValue()
    {
        var socket = Mock.Of<IMinecraftSocket>();
        var loading = service.GetSolved(socket, "1");

        service.Release(socket);
        var state = await loading;

        await WaitFor(() => state.SolvedTutorials.Value == null);
        state.SolvedTutorials.Value.Should().BeNull();
    }
}
