using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// Turns a disconnect snapshot into a stored row: checks eligibility, throttles per user, loads the
/// three sources independently for the last day and upserts what succeeded.
/// </summary>
public class RealizedProfitCollector
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(1);
    public static readonly TimeSpan Throttle = TimeSpan.FromHours(1);
    private const int PruneAbove = 5000;
    private readonly IRealizedProfitSources sources;
    private readonly IRealizedProfitStore store;
    private readonly TimeProvider time;
    private readonly ILogger<RealizedProfitCollector> logger;
    private readonly ConcurrentDictionary<string, DateTime> lastProcessed = new();

    public RealizedProfitCollector(IRealizedProfitSources sources, IRealizedProfitStore store,
        TimeProvider time, ILogger<RealizedProfitCollector> logger)
    {
        this.sources = sources;
        this.store = store;
        this.time = time;
        this.logger = logger;
    }

    /// <returns>true when a row was written</returns>
    public async Task<bool> Process(RealizedProfitSnapshot snapshot, CancellationToken token = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (!RealizedProfitEligibility.IsEligible(snapshot, now))
            return false;
        if (!TryClaim(snapshot.UserId, now))
            return false;
        var row = await LoadRow(snapshot, now, token);
        if (row.AhProfit == null && row.BazaarProfit == null && row.TaskValue == null)
        {
            lastProcessed.TryRemove(snapshot.UserId, out _); // nothing stored, allow a retry on the next disconnect
            return false;
        }
        await store.Upsert(row);
        return true;
    }

    /// <summary>Marks the user as processed now unless that already happened within the throttle window.</summary>
    private bool TryClaim(string userId, DateTime now)
    {
        PruneIfLarge(now);
        var claimed = false;
        lastProcessed.AddOrUpdate(userId, _ =>
        {
            claimed = true;
            return now;
        }, (_, last) =>
        {
            if (now - last < Throttle)
                return last;
            claimed = true;
            return now;
        });
        return claimed;
    }

    private void PruneIfLarge(DateTime now)
    {
        if (lastProcessed.Count < PruneAbove)
            return;
        foreach (var (user, last) in lastProcessed.Where(e => now - e.Value >= Throttle).ToList())
            lastProcessed.TryRemove(user, out _);
    }

    private async Task<RealizedProfitRow> LoadRow(RealizedProfitSnapshot snapshot, DateTime now, CancellationToken token)
    {
        var from = now - Window;
        var ah = Load("ah", () => sources.GetAhProfit(snapshot.McUuids, from, now, token), snapshot);
        var bazaar = Load("bazaar", () => sources.GetBazaarProfit(snapshot.McUuids, from, now, token), snapshot);
        var tasks = Load("tasks", () => sources.GetTaskValue(snapshot.McUuids, from, now, token), snapshot);
        return new RealizedProfitRow
        {
            Tier = snapshot.Tier,
            UserId = snapshot.UserId,
            AhProfit = await ah,
            BazaarProfit = await bazaar,
            TaskValue = await tasks,
            UpdatedAt = now
        };
    }

    private async Task<long?> Load(string source, Func<Task<long>> load, RealizedProfitSnapshot snapshot)
    {
        try
        {
            return await load();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load {source} realized profit of user {user}", source, snapshot.UserId);
            return null;
        }
    }
}
