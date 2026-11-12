using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Accepts a flip only once per window even when the same flip is delivered concurrently
/// by several brokers. Acceptance is decided by an atomic add or compare-and-swap of the timestamp.
/// </summary>
public class RecentFlipFilter(TimeSpan window)
{
    private readonly ConcurrentDictionary<(string, LowPricedAuction.FinderType, long), DateTime> seen = new();

    /// <summary>True for exactly one caller per key and window</summary>
    public bool TryAccept((string, LowPricedAuction.FinderType, long) key, DateTime now)
    {
        if (seen.TryAdd(key, now))
            return true;
        if (!seen.TryGetValue(key, out var last))
            return seen.TryAdd(key, now);
        if (last > now - window)
            return false;
        return seen.TryUpdate(key, now, last);
    }

    /// <summary>Number of tracked flips; exposed so pruning can be verified</summary>
    public int Count => seen.Count;

    public void RemoveOlderThan(DateTime cutoff)
    {
        foreach (var item in seen)
        {
            if (item.Value < cutoff)
                ((ICollection<KeyValuePair<(string, LowPricedAuction.FinderType, long), DateTime>>)seen).Remove(item);
        }
    }
}
