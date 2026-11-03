#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// Scylla table realized_profit_by_tier_day: partition key (tier, day), clustering key user_id.
/// Rows expire after 2 days through the table default ttl and gc_grace is low, so expired rows do not pile up
/// as tombstones; the endpoint only reads today's and yesterday's partition.
/// The ISession is resolved lazily, an unreachable Scylla must not stop the host from starting.
/// </summary>
public class RealizedProfitStore : IRealizedProfitStore
{
    public const string TableName = "realized_profit_by_tier_day";
    public const int TtlSeconds = 2 * 24 * 3600;
    public const int GcGraceSeconds = 3 * 3600;
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);
    private static readonly string CreateCql = $"CREATE TABLE IF NOT EXISTS {TableName} (tier int, day int, user_id text, "
        + "ah_profit bigint, bazaar_profit bigint, task_value bigint, missing int, updated_at timestamp, "
        + $"PRIMARY KEY ((tier, day), user_id)) WITH default_time_to_live = {TtlSeconds} AND gc_grace_seconds = {GcGraceSeconds}";
    private readonly IServiceProvider services;
    private readonly TimeProvider time;
    private readonly ILogger<RealizedProfitStore> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Table<RealizedProfitDbRow>? table;
    private DateTime retryNotBefore;

    public RealizedProfitStore(IServiceProvider services, TimeProvider time, ILogger<RealizedProfitStore> logger)
    {
        this.services = services;
        this.time = time;
        this.logger = logger;
    }

    /// <summary>The table, or null when scylla is not available right now (logged, retried after a minute).</summary>
    private async Task<Table<RealizedProfitDbRow>?> GetTable()
    {
        if (table != null)
            return table;
        await gate.WaitAsync();
        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (table != null || now < retryNotBefore)
                return table;
            return table = await CreateTable();
        }
        catch (Exception e)
        {
            retryNotBefore = time.GetUtcNow().UtcDateTime + RetryAfterFailure;
            logger.LogError(e, "Realized profit store is not available, skipping for a minute");
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Table<RealizedProfitDbRow>> CreateTable()
    {
        var session = services.GetRequiredService<ISession>();
        await session.ExecuteAsync(new SimpleStatement(CreateCql));
        return new Table<RealizedProfitDbRow>(session, new MappingConfiguration().Define(
            new Map<RealizedProfitDbRow>()
                .TableName(TableName)
                .PartitionKey(r => r.Tier, r => r.Day)
                .ClusteringKey(r => r.UserId)
                .Column(r => r.Tier, c => c.WithName("tier"))
                .Column(r => r.Day, c => c.WithName("day"))
                .Column(r => r.UserId, c => c.WithName("user_id"))
                .Column(r => r.AhProfit, c => c.WithName("ah_profit"))
                .Column(r => r.BazaarProfit, c => c.WithName("bazaar_profit"))
                .Column(r => r.TaskValue, c => c.WithName("task_value"))
                .Column(r => r.Missing, c => c.WithName("missing"))
                .Column(r => r.UpdatedAt, c => c.WithName("updated_at"))));
    }

    public async Task Upsert(RealizedProfitRow row)
    {
        var t = await GetTable();
        if (t == null)
            return;
        try
        {
            await t.Insert(RealizedProfitDbRow.From(row)).SetTTL(TtlSeconds).ExecuteAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not store realized profit of user {user}", row.UserId);
        }
    }

    /// <summary>
    /// Rows of the last 24 hours, newest row per user when it appears in both day partitions.
    /// Throws when scylla is unavailable or the read fails, an empty list means there really are no rows.
    /// </summary>
    public async Task<List<RealizedProfitRow>> GetTier(int tier)
    {
        var t = await GetTable();
        if (t == null)
            throw new InvalidOperationException("Realized profit store is not available");
        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            var days = new[] { RealizedProfitDbRow.DayOf(now), RealizedProfitDbRow.DayOf(now.AddDays(-1)) };
            var perDay = await Task.WhenAll(days.Select(d => ReadDay(t, tier, d)));
            return Latest(perDay.SelectMany(r => r), now - RealizedProfitAggregator.MaxAge);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not read realized profit of tier {tier}", tier);
            throw;
        }
    }

    private static async Task<List<RealizedProfitDbRow>> ReadDay(Table<RealizedProfitDbRow> t, int tier, int day)
        => (await t.Where(r => r.Tier == tier && r.Day == day).ExecuteAsync()).ToList();

    public static List<RealizedProfitRow> Latest(IEnumerable<RealizedProfitDbRow> rows, DateTime since)
    {
        return rows.Where(r => r.UpdatedAt >= since)
            .GroupBy(r => r.UserId)
            .Select(g => g.MaxBy(r => r.UpdatedAt)!.ToRow())
            .ToList();
    }
}
