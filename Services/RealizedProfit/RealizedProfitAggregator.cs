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
    /// <summary>users for which at least one source could not be loaded, their totals are lower bounds</summary>
    public int UsersWithMissingSources { get; set; }
}

public static class RealizedProfitAggregator
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

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
            UsersWithMissingSources = fresh.Count(r => r.AhProfit == null || r.BazaarProfit == null || r.TaskValue == null)
        };
    }
}
