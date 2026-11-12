using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class DelayExemptRefresherTests
{
    private DelayExemptionList list;
    private int fetches;
    private Func<int, HashSet<(string, string)>> responder;

    [SetUp]
    public void Setup()
    {
        list = new DelayExemptionList();
        fetches = 0;
        responder = _ => [];
    }

    private Task<HashSet<(string, string)>> Fetch(CancellationToken token)
    {
        fetches++;
        return Task.FromResult(responder(fetches));
    }

    private DelayExemptRefresher Create(Func<TimeSpan, CancellationToken, Task> delay)
        => new(Fetch, list, NullLogger.Instance, delay);

    [Test]
    public async Task KeyAppearingOnSecondFetchEndsUpInTheList()
    {
        responder = n => n == 1 ? [("A", "1")] : [("A", "1"), ("B", "2")];
        using var cts = new CancellationTokenSource();
        var delays = 0;
        var refresher = Create((_, _) =>
        {
            if (++delays == 2)
                cts.Cancel();
            return Task.CompletedTask;
        });

        await refresher.RunAsync(TimeSpan.FromMinutes(10), cts.Token);

        Assert.That(fetches, Is.EqualTo(2));
        Assert.That(list.Exemptions, Does.Contain(("B", "2")));
    }

    [Test]
    public async Task FailingFetchKeepsPreviousKeys()
    {
        responder = n => n == 1 ? [("A", "1")] : throw new InvalidOperationException("tracker down");
        var refresher = Create((_, _) => Task.CompletedTask);

        await refresher.RefreshOnce(CancellationToken.None);
        await refresher.RefreshOnce(CancellationToken.None);

        Assert.That(list.Exemptions, Is.EquivalentTo(new[] { ("A", "1") }));
    }

    [Test]
    public async Task RefreshReplacesTheSetInsteadOfMutatingIt()
    {
        responder = _ => [("B", "2")];
        var before = list.Exemptions;

        await Create((_, _) => Task.CompletedTask).RefreshOnce(CancellationToken.None);

        Assert.That(before, Is.Empty);
        Assert.That(list.Exemptions, Is.Not.SameAs(before));
    }

    [Test]
    public async Task StopsCleanlyOnCancellationWhileWaiting()
    {
        using var cts = new CancellationTokenSource();
        var refresher = Create((_, token) => Task.Delay(Timeout.Infinite, token));
        var run = refresher.RunAsync(TimeSpan.FromMinutes(10), cts.Token);

        cts.Cancel();
        await run;

        Assert.That(run.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task EmptyFetchKeepsPreviousKeys()
    {
        responder = n => n == 1 ? [("A", "1")] : [];
        var refresher = Create((_, _) => Task.CompletedTask);

        await refresher.RefreshOnce(CancellationToken.None);
        await refresher.RefreshOnce(CancellationToken.None);

        Assert.That(list.Exemptions, Is.EquivalentTo(new[] { ("A", "1") }));
    }

    [Test]
    public async Task RetriesSoonWhileTheListIsEmptyAndWaitsFullIntervalAfterwards()
    {
        responder = n => n == 1 ? [] : [("A", "1")];
        using var cts = new CancellationTokenSource();
        var waits = new List<TimeSpan>();
        var refresher = Create((wait, _) =>
        {
            waits.Add(wait);
            if (waits.Count == 2)
                cts.Cancel();
            return Task.CompletedTask;
        });

        await refresher.RunAsync(TimeSpan.FromMinutes(10), cts.Token);

        Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10) }));
    }

    [Test]
    public async Task ServiceStartsPeriodicRefreshEveryTenMinutes()
    {
        responder = n => [("K", n.ToString())];
        using var cts = new CancellationTokenSource();
        var waits = new List<TimeSpan>();

        await ModBackgroundService.StartDelayExemptRefresh(Fetch, list, NullLogger.Instance, cts.Token, (wait, _) =>
        {
            waits.Add(wait);
            if (waits.Count == 2)
                cts.Cancel();
            return Task.CompletedTask;
        });

        Assert.That(fetches, Is.EqualTo(2));
        Assert.That(waits, Is.All.EqualTo(TimeSpan.FromMinutes(10)));
        Assert.That(list.Exemptions, Does.Contain(("K", "2")));
    }
}
