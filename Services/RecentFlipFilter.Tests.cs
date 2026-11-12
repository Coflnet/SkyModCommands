using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class RecentFlipFilterTests
{
    private static readonly (string, LowPricedAuction.FinderType, long) Key = ("uuid", LowPricedAuction.FinderType.SNIPER, 1_000_000);

    [Test]
    public void ConcurrentDeliveryOfSameFlipIsAcceptedOnce()
    {
        for (var round = 0; round < 50; round++)
        {
            var filter = new RecentFlipFilter(TimeSpan.FromMinutes(1));
            var now = DateTime.UtcNow;
            using var start = new ManualResetEventSlim();
            var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            {
                start.Wait();
                return filter.TryAccept(Key, now);
            })).ToArray();
            start.Set();
            Task.WaitAll(tasks);
            Assert.That(tasks.Count(t => t.Result), Is.EqualTo(1));
        }
    }

    [Test]
    public void ConcurrentDeliveryOfExpiredFlipIsAcceptedOnce()
    {
        for (var round = 0; round < 50; round++)
        {
            var filter = new RecentFlipFilter(TimeSpan.FromMinutes(1));
            var old = DateTime.UtcNow - TimeSpan.FromMinutes(5);
            filter.TryAccept(Key, old);
            using var start = new ManualResetEventSlim();
            var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            {
                start.Wait();
                return filter.TryAccept(Key, DateTime.UtcNow);
            })).ToArray();
            start.Set();
            Task.WaitAll(tasks);
            Assert.That(tasks.Count(t => t.Result), Is.EqualTo(1));
        }
    }

    [Test]
    public void ExpiredEntryIsAcceptedAgain()
    {
        var filter = new RecentFlipFilter(TimeSpan.FromMinutes(1));
        var now = DateTime.UtcNow;
        Assert.That(filter.TryAccept(Key, now), Is.True);
        Assert.That(filter.TryAccept(Key, now.AddSeconds(30)), Is.False);
        Assert.That(filter.TryAccept(Key, now.AddSeconds(61)), Is.True);
        Assert.That(filter.TryAccept(Key, now.AddSeconds(62)), Is.False);
    }

    [Test]
    public void DifferentTargetPriceIsDifferentFlip()
    {
        var filter = new RecentFlipFilter(TimeSpan.FromMinutes(1));
        var now = DateTime.UtcNow;
        Assert.That(filter.TryAccept(Key, now), Is.True);
        Assert.That(filter.TryAccept((Key.Item1, Key.Item2, 2_000_000), now), Is.True);
    }

    [Test]
    public void RemoveOlderThanKeepsFreshEntries()
    {
        var filter = new RecentFlipFilter(TimeSpan.FromMinutes(1));
        var now = DateTime.UtcNow;
        filter.TryAccept(Key, now.AddMinutes(-10));
        filter.TryAccept(("other", Key.Item2, 1), now);
        filter.RemoveOlderThan(now.AddMinutes(-2));
        Assert.That(filter.TryAccept(Key, now), Is.True);
        Assert.That(filter.TryAccept(("other", Key.Item2, 1), now), Is.False);
    }
}
