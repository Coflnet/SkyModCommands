using System;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

public class TierRealizedProfit
{
    public int Tier { get; set; }
    public string TierName { get; set; } = "";
    public int Users { get; set; }
    public long AhProfit { get; set; }
    public long BazaarProfit { get; set; }
    public long TaskValue { get; set; }
    public long Total { get; set; }
    public double AveragePerUser { get; set; }
    /// <summary>median of the per user totals, not skewed by single whales</summary>
    public double MedianPerUser { get; set; }
    /// <summary>users for which at least one source could not be loaded, their totals are lower bounds</summary>
    public int UsersWithMissingSources { get; set; }
}

public static class RealizedProfitAggregator
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    /// <summary>Tier id of the aggregate over every tier</summary>
    public const int AllTierId = -1;
    /// <summary>Tier id of the aggregate over all paid tiers (STARTER_PREMIUM and above)</summary>
    public const int PaidTierId = -2;
    public const string AllName = "ALL";
    public const string PaidName = "PAID";

    private static long Total(RealizedProfitRow r) => (r.AhProfit ?? 0) + (r.BazaarProfit ?? 0) + (r.TaskValue ?? 0);

    /// <summary>Median of the values, the mean of the two middle ones for an even count, 0 when empty.</summary>
    public static double Median(IReadOnlyCollection<long> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : ((double)sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>
    /// A user who changed tier within the window has a row in each tier, each with the full profit.
    /// Keeps only the newest row per user so every user is counted once, in their latest tier.
    /// </summary>
    public static List<RealizedProfitRow> LatestPerUser(IEnumerable<RealizedProfitRow> rows)
        => rows.GroupBy(r => r.UserId).Select(g => g.MaxBy(r => r.UpdatedAt)!).ToList();

    /// <summary>Sums the rows of one tier, ignoring rows older than a day in case the table ttl was raised.</summary>
    public static TierRealizedProfit Aggregate(int tier, string tierName, IEnumerable<RealizedProfitRow> rows, DateTime now)
    {
        var fresh = rows.Where(r => r.UpdatedAt >= now - MaxAge).ToList();
        var ah = fresh.Sum(r => r.AhProfit ?? 0);
        var bazaar = fresh.Sum(r => r.BazaarProfit ?? 0);
        var tasks = fresh.Sum(r => r.TaskValue ?? 0);
        var total = ah + bazaar + tasks;
        return new TierRealizedProfit
        {
            Tier = tier,
            TierName = tierName,
            Users = fresh.Count,
            AhProfit = ah,
            BazaarProfit = bazaar,
            TaskValue = tasks,
            Total = total,
            AveragePerUser = fresh.Count == 0 ? 0 : (double)total / fresh.Count,
            MedianPerUser = Median(fresh.Select(Total).ToList()),
            UsersWithMissingSources = fresh.Count(r => r.AhProfit == null || r.BazaarProfit == null || r.TaskValue == null)
        };
    }
}
