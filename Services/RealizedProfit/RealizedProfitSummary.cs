using System;
using System.Collections.Generic;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>Per tier rows plus the ALL and PAID aggregates, all computed from the same rows.</summary>
public class RealizedProfitSummary
{
    public List<TierRealizedProfit> Tiers { get; set; } = [];
    /// <summary>every tier including NONE</summary>
    public TierRealizedProfit All { get; set; } = new();
    /// <summary>STARTER_PREMIUM and above</summary>
    public TierRealizedProfit Paid { get; set; } = new();
    public int WindowHours { get; set; } = (int)RealizedProfitAggregator.MaxAge.TotalHours;
    public DateTime GeneratedAt { get; set; }
}
