using System;
using System.Collections.Concurrent;
using System.Reflection;
using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class PreApiHoldBackTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 30, DateTimeKind.Utc);

    private static (PreApiService service, LowPricedAuction flip) Create(bool sentToSomeone = true)
    {
        var service = new PreApiService(null, new FlipperService(null, null), NullLogger<PreApiService>.Instance, null, null, null);
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
    public void SentFlipIsHeldBackEvenWhenAnApiUpdateLandsInsideItsBed()
    {
        // regression: auctions younger than ~20s are not in the public api update, an update inside the bed doesn't make it public
        var (service, flip) = Create();
        service.ShouldHoldBack(flip).Should().BeTrue();
    }

    [Test]
    public void NotSentIsNeverHeldBack()
    {
        var (service, flip) = Create(sentToSomeone: false);
        service.ShouldHoldBack(flip).Should().BeFalse();
    }
}
