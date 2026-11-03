using System;
using System.Linq;
using System.Threading;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;

public enum ApiRelativeOutcome
{
    Skipped,
    Observed,
    Outlier
}

/// <summary>
/// Records when a flip was sent to a user relative to the arrival of the api update that made the auction public.
/// Values further than 120s away are counted as outliers instead of poisoning the histogram sum.
/// </summary>
public static class ApiRelativeSendMetric
{
    public const string ReplayProp = "replay";
    private static readonly long SampleIntervalTicks = TimeSpan.FromMinutes(1).Ticks;
    private static long lastSampleTicks;

    private static readonly Prometheus.Histogram sendRelativeApi = Prometheus.Metrics.CreateHistogram(
        "sky_mod_flip_send_relative_api_seconds", "Seconds a flip was sent relative to the arrival of the api update data (first new auction, negative = before). bed=true flips were still in their bed and are held on purpose",
        new Prometheus.HistogramConfiguration
        {
            LabelNames = new[] { "tier", "source", "bed" },
            Buckets = ApiUpdateClock.RelativeBuckets
        });
    private static readonly Prometheus.Counter outliers = Prometheus.Metrics.CreateCounter(
        "sky_mod_flip_send_relative_api_outliers_total", "Sends more than 120s away from the api arrival, not part of the histogram",
        new Prometheus.CounterConfiguration { LabelNames = new[] { "tier", "source", "direction" } });

    public static double OutlierCount(string tier, string source, string direction)
        => outliers.WithLabels(tier, source, direction).Value;

    /// <summary>Replayed flips are sent long after they were found on purpose and say nothing about latency</summary>
    public static bool IsReplay(LowPricedAuction flip)
        => flip.AdditionalProps?.ContainsKey(ReplayProp) ?? false;

    public static ApiRelativeOutcome Record(string tier, LowPricedAuction flip, ApiUpdateClock clock, DateTime now, Action<string> log = null)
    {
        if (clock == null || IsReplay(flip))
            return ApiRelativeOutcome.Skipped;
        var result = clock.ApiRelative(flip, now);
        if (result is not ApiRelativeResult r)
            return ApiRelativeOutcome.Skipped;
        if (ApiUpdateClock.Plausible(r.Seconds))
        {
            sendRelativeApi.WithLabels(tier, r.Source, r.InBed ? "true" : "false").Observe(r.Seconds);
            return ApiRelativeOutcome.Observed;
        }
        outliers.WithLabels(tier, r.Source, r.Seconds < 0 ? "early" : "late").Inc();
        LogSample(flip, clock, r, now, log ?? (m => dev.Logger.Instance.Info(m)));
        return ApiRelativeOutcome.Outlier;
    }

    /// <summary>Logs at most one outlier per minute and process</summary>
    private static void LogSample(LowPricedAuction flip, ApiUpdateClock clock, ApiRelativeResult r, DateTime now, Action<string> log)
    {
        var current = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref lastSampleTicks);
        if (current - last < SampleIntervalTicks || Interlocked.CompareExchange(ref lastSampleTicks, current, last) != last)
            return;
        log(Describe(flip, clock, r, now));
    }

    internal static void ResetSampleLimit() => Interlocked.Exchange(ref lastSampleTicks, 0);

    internal static string Describe(LowPricedAuction flip, ApiUpdateClock clock, ApiRelativeResult r, DateTime now)
    {
        var a = flip.Auction;
        var reference = now - TimeSpan.FromSeconds(r.Seconds);
        return $"Implausible api relative send {r.Seconds:0.0}s source={r.Source} finder={flip.Finder} uuid={a.Uuid} "
            + $"start={a.Start:O} ({a.Start.Kind}) findTime={a.FindTime:O} ({a.FindTime.Kind}) reference={reference:O} now={now:O} "
            + $"lastUpdate={clock.LastUpdate:O} "
            + $"auctionContext=[{string.Join(",", a.Context?.Keys ?? Enumerable.Empty<string>())}] "
            + $"additionalProps=[{string.Join(",", flip.AdditionalProps?.Keys ?? Enumerable.Empty<string>())}]";
    }
}
