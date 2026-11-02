using System;
using System.Collections.Concurrent;
using System.Reflection;
using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class PreApiPublicBedTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 30, DateTimeKind.Utc);

    private static (PreApiService service, LowPricedAuction flip) Create(DateTime? lastApiUpdate, bool sentToSomeone = true)
    {
        var clock = new ApiUpdateClock();
        if (lastApiUpdate != null)
            clock.Set(lastApiUpdate.Value);
        var service = new PreApiService(null, new FlipperService(null, null), NullLogger<PreApiService>.Instance, null, null, null, clock);
        var flip = new LowPricedAuction { Auction = new SaveAuction { Uuid = "abc", Start = Start } };
        if (sentToSomeone)
        {
            var sent = (ConcurrentDictionary<string, DateTime>)typeof(PreApiService)
                .GetField("sent", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(service);
            sent[flip.Auction.Uuid] = DateTime.UtcNow;
        }
        return (service, flip);
    }

    [Test]
    public void PublicBedIsNotHeldBack()
    {
        // updates at :45 each minute, the next is 12:00:45 which is within Start(12:00:30)+20s+margin
        var (service, flip) = Create(new DateTime(2026, 1, 1, 11, 59, 45, DateTimeKind.Utc));
        service.ShouldHoldBack(flip).Should().BeFalse();
    }

    [Test]
    public void NonPublicBedIsHeldBackWhenSent()
    {
        var (service, flip) = Create(new DateTime(2026, 1, 1, 11, 59, 0, DateTimeKind.Utc));
        service.ShouldHoldBack(flip).Should().BeTrue();
    }

    [Test]
    public void NotSentIsNeverHeldBack()
    {
        var (service, flip) = Create(null, sentToSomeone: false);
        service.ShouldHoldBack(flip).Should().BeFalse();
    }
}
