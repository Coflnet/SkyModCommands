using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[NonParallelizable]
public class ApiRelativeSendMetricTests
{
    private static readonly DateTime LastUpdated = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private ApiUpdateClock clock;
    private List<string> logs;

    [SetUp]
    public void Setup()
    {
        clock = new ApiUpdateClock();
        clock.SetLastUpdated(LastUpdated);
        logs = new();
        ApiRelativeSendMetric.ResetSampleLimit();
    }

    private static LowPricedAuction ApiFlip(DateTime findTime, Dictionary<string, string> props = null) => new()
    {
        Auction = new SaveAuction { Uuid = "u", Start = findTime.AddSeconds(-100), FindTime = findTime, Context = new() },
        AdditionalProps = props ?? new()
    };

    private ApiRelativeOutcome Record(string tier, LowPricedAuction flip, DateTime now)
        => ApiRelativeSendMetric.Record(tier, flip, clock, now, logs.Add);

    [Test]
    public void PlausibleSendIsObserved()
    {
        Record("T1", ApiFlip(LastUpdated), LastUpdated.AddSeconds(9)).Should().Be(ApiRelativeOutcome.Observed);
        logs.Should().BeEmpty();
    }

    [Test]
    public void SendHoursLateIsCountedAsLateOutlierAndNotObserved()
    {
        var before = ApiRelativeSendMetric.OutlierCount("T2", "api", "late");

        Record("T2", ApiFlip(LastUpdated), LastUpdated.AddHours(2)).Should().Be(ApiRelativeOutcome.Outlier);

        ApiRelativeSendMetric.OutlierCount("T2", "api", "late").Should().Be(before + 1);
    }

    [Test]
    public void SendHoursEarlyIsCountedAsEarlyOutlier()
    {
        var before = ApiRelativeSendMetric.OutlierCount("T3", "api", "early");

        Record("T3", ApiFlip(LastUpdated), LastUpdated.AddHours(-2)).Should().Be(ApiRelativeOutcome.Outlier);

        ApiRelativeSendMetric.OutlierCount("T3", "api", "early").Should().Be(before + 1);
    }

    [Test]
    public void OutlierSampleIsLoggedOncePerMinuteWithDiagnostics()
    {
        var flip = ApiFlip(LastUpdated, new() { { "bed", "x" } });
        flip.Auction.Context["pre-x"] = "y";

        Record("T4", flip, LastUpdated.AddHours(2));
        Record("T4", flip, LastUpdated.AddHours(2));

        logs.Should().ContainSingle();
        logs[0].Should().Contain("uuid=u").And.Contain("(Utc)").And.Contain("pre-x").And.Contain("bed");
    }

    [Test]
    public void ReplayedFlipIsNeitherObservedNorCountedAsOutlier()
    {
        var before = ApiRelativeSendMetric.OutlierCount("T5", "api", "late");
        var flip = ApiFlip(LastUpdated, new() { { ApiRelativeSendMetric.ReplayProp, "" } });

        Record("T5", flip, LastUpdated.AddHours(30)).Should().Be(ApiRelativeOutcome.Skipped);

        ApiRelativeSendMetric.OutlierCount("T5", "api", "late").Should().Be(before);
        logs.Should().BeEmpty();
    }

    [Test]
    public void MissingClockIsSkipped()
    {
        ApiRelativeSendMetric.Record("T6", ApiFlip(LastUpdated), null, LastUpdated, logs.Add).Should().Be(ApiRelativeOutcome.Skipped);
    }

    [Test]
    public void RustFlipsAreSkippedEvenHoursAway()
    {
        var rust = ApiFlip(LastUpdated);
        rust.Finder = LowPricedAuction.FinderType.Rust;

        Record("T7", rust, LastUpdated.AddHours(2)).Should().Be(ApiRelativeOutcome.Skipped);
        logs.Should().BeEmpty();
    }
}
