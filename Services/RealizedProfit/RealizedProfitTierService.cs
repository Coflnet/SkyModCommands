using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccountTier = Coflnet.Sky.Commands.Shared.AccountTier;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>Aggregates the stored rows per tier and caches the result, the endpoint is publicly reachable.</summary>
public class RealizedProfitTierService
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private readonly IRealizedProfitStore store;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim gate = new(1, 1);
    private RealizedProfitSummary? cached;
    private DateTime cachedAt;

    public RealizedProfitTierService(IRealizedProfitStore store, TimeProvider time)
    {
        this.store = store;
        this.time = time;
    }

    public async Task<List<TierRealizedProfit>> GetTiers() => (await GetSummary()).Tiers;

    public async Task<RealizedProfitSummary> GetSummary()
    {
        if (TryGetCached(out var hit))
            return hit;
        await gate.WaitAsync();
        try
        {
            if (TryGetCached(out hit))
                return hit;
            var now = time.GetUtcNow().UtcDateTime;
            cached = await Load(now);
            cachedAt = now;
            return cached;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetCached(out RealizedProfitSummary result)
    {
        var local = cached;
        result = local!;
        return local != null && time.GetUtcNow().UtcDateTime - cachedAt < CacheDuration;
    }

    /// <summary>Throws if any tier could not be read, so partial numbers are neither cached nor exported.</summary>
    private async Task<RealizedProfitSummary> Load(DateTime now)
    {
        var tiers = Enum.GetValues<AccountTier>();
        var loaded = await Task.WhenAll(tiers.Select(t => store.GetTier((int)t)));
        var rows = RealizedProfitAggregator.LatestPerUser(loaded.SelectMany(r => r));
        var byTier = rows.ToLookup(r => r.Tier);
        return new RealizedProfitSummary
        {
            Tiers = tiers.Select(t => RealizedProfitAggregator.Aggregate((int)t, t.ToString(), byTier[(int)t], now)).ToList(),
            All = RealizedProfitAggregator.Aggregate(RealizedProfitAggregator.AllTierId, RealizedProfitAggregator.AllName, rows, now),
            Paid = RealizedProfitAggregator.Aggregate(RealizedProfitAggregator.PaidTierId, RealizedProfitAggregator.PaidName,
                rows.Where(r => r.Tier >= (int)AccountTier.STARTER_PREMIUM), now),
            GeneratedAt = now
        };
    }
}
