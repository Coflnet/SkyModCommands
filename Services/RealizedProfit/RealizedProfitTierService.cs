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
    private List<TierRealizedProfit>? cached;
    private DateTime cachedAt;

    public RealizedProfitTierService(IRealizedProfitStore store, TimeProvider time)
    {
        this.store = store;
        this.time = time;
    }

    public async Task<List<TierRealizedProfit>> GetTiers()
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

    private bool TryGetCached(out List<TierRealizedProfit> result)
    {
        result = cached!;
        return cached != null && time.GetUtcNow().UtcDateTime - cachedAt < CacheDuration;
    }

    private async Task<List<TierRealizedProfit>> Load(DateTime now)
    {
        var tiers = Enum.GetValues<AccountTier>();
        var rows = await Task.WhenAll(tiers.Select(async t => (tier: t, rows: await store.GetTier((int)t))));
        return rows.Select(r => RealizedProfitAggregator.Aggregate((int)r.tier, r.tier.ToString(), r.rows, now)).ToList();
    }
}
