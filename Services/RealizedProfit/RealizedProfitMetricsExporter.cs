using System;
using System.Threading;
using System.Threading.Tasks;
using Prometheus;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// Publishes the realized profit numbers as gauges so Grafana can keep them over time.
/// The store is shared and every replica exports the same values, query with <c>max by (tier)</c> (not sum or avg across pods).
/// </summary>
public class RealizedProfitMetricsExporter
{
    private readonly RealizedProfitTierService tiers;
    private readonly Gauge users;
    private readonly Gauge total;
    private readonly Gauge perUser;
    private readonly Gauge bySource;
    private readonly Gauge missing;
    private readonly Gauge generated;

    public RealizedProfitMetricsExporter(RealizedProfitTierService tiers, MetricFactory? factory = null)
    {
        this.tiers = tiers;
        factory ??= Metrics.DefaultFactory;
        users = factory.CreateGauge("sky_mod_realized_profit_users", "Users with realized profit data in the last day", "tier");
        total = factory.CreateGauge("sky_mod_realized_profit_total", "Summed realized profit of the last day", "tier");
        perUser = factory.CreateGauge("sky_mod_realized_profit_per_user", "Realized profit per user of the last day, stat is mean or median", "tier", "stat");
        missing = factory.CreateGauge("sky_mod_realized_profit_users_missing_sources", "Users for which at least one source could not be loaded, their totals are lower bounds", "tier");
        generated = factory.CreateGauge("sky_mod_realized_profit_generated_timestamp_seconds", "Unix time of the last successful refresh of the realized profit gauges");
        bySource = factory.CreateGauge("sky_mod_realized_profit_source_total", "Summed realized profit of the last day per source (ah, bazaar, tasks)", "tier", "source");
    }

    /// <summary>Throws if the numbers could not be loaded, the gauges then keep their previous values.</summary>
    public async Task Refresh(CancellationToken token = default)
    {
        var summary = await tiers.GetSummary().WaitAsync(token);
        foreach (var tier in summary.Tiers)
            Export(tier);
        Export(summary.All);
        Export(summary.Paid);
        generated.Set(new DateTimeOffset(summary.GeneratedAt).ToUnixTimeSeconds());
    }

    private void Export(TierRealizedProfit tier)
    {
        var name = tier.TierName;
        users.WithLabels(name).Set(tier.Users);
        missing.WithLabels(name).Set(tier.UsersWithMissingSources);
        total.WithLabels(name).Set(tier.Total);
        perUser.WithLabels(name, "mean").Set(tier.AveragePerUser);
        perUser.WithLabels(name, "median").Set(tier.MedianPerUser);
        bySource.WithLabels(name, "ah").Set(tier.AhProfit);
        bySource.WithLabels(name, "bazaar").Set(tier.BazaarProfit);
        bySource.WithLabels(name, "tasks").Set(tier.TaskValue);
    }
}
