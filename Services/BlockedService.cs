using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging;
using System.Linq;
using static Coflnet.Sky.ModCommands.Services.BlockedService;

namespace Coflnet.Sky.ModCommands.Services;

public interface IBlockedService
{
    Task ArchiveBlockedFlipsUntil(ConcurrentQueue<MinecraftSocket.BlockedElement> topBlocked, string userId, int v);
    Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, Guid auctionUuid);
    /// <summary>
    /// Reads recorded blocked reasons of one user (single partition read)
    /// </summary>
    /// <param name="userId">Coflnet account id</param>
    /// <param name="limit">maximum rows, capped at <see cref="BlockedService.MaxReadLimit"/></param>
    Task<List<BlockedReason>> GetBlockedReasons(string userId, int limit);
}

public class BlockedService : IBlockedService
{
    Table<BlockedReason> table;
    ISession session;
    private readonly ILogger<BlockedService> logger;
    public const int MaxReadLimit = 20000;

    public BlockedService(ISession session, ILogger<BlockedService> logger)
    {
        this.logger = logger;
        var mapping = new MappingConfiguration().Define(
            new Map<BlockedReason>()
                .TableName("blocked_reasons")
                .PartitionKey(x => x.UserId)
                .ClusteringKey(x => x.AuctionUuid)
                .ClusteringKey(x => x.FinderType)
                .Column(o => o.FinderType, c => c.WithName("finder_type").WithDbType<int>())
                .Column(x => x.BlockedAt, cm => cm.WithName("blocked_at"))
                .Column(x => x.Reason, cm => cm.WithName("reason"))
                .Column(x => x.TargetPrice, cm => cm.WithName("target_price"))
                .Column(x => x.StartingBid, cm => cm.WithName("starting_bid"))
        );
        table = new Table<BlockedReason>(session, mapping);
        table.CreateIfNotExists();
        this.session = session;
        // the table exists in production without these columns
        foreach (var column in new[] { "target_price", "starting_bid" })
        {
            try
            {
                session.Execute($"ALTER TABLE blocked_reasons ADD {column} bigint");
            }
            catch (Exception e) when (IsColumnExistsError(e))
            {
                // already migrated
            }
        }
    }

    /// <summary>
    /// True if the exception is the error cassandra/scylla throw when adding an already existing column
    /// </summary>
    internal static bool IsColumnExistsError(Exception e)
    {
        var message = e?.Message ?? "";
        return e is InvalidQueryException
            && (message.Contains("conflicts with an existing column", StringComparison.OrdinalIgnoreCase)
                || message.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Only auctions that started recently are archived, old ones are replayed flips
    /// </summary>
    internal static bool ShouldArchive(MinecraftSocket.BlockedElement blocked, DateTime now)
    {
        return blocked?.Flip?.Auction != null && blocked.Flip.Auction.Start >= now.AddHours(-2);
    }

    public async Task<List<BlockedReason>> GetBlockedReasons(string userId, int limit)
    {
        limit = Math.Clamp(limit, 1, MaxReadLimit);
        return (await table.Where(x => x.UserId == userId).Take(limit).ExecuteAsync()).ToList();
    }

    public async Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, Guid auctionUuid)
    {
        return await table.Where(x => x.UserId == userId && x.AuctionUuid == auctionUuid).ExecuteAsync();
    }

    private const string InsertBlockedReasonCql =
        "INSERT INTO blocked_reasons (userid, auctionuuid, finder_type, blocked_at, reason, target_price, starting_bid) VALUES (?, ?, ?, ?, ?, ?, ?) USING TTL ?";

    public async Task ArchiveBlockedFlipsUntil(ConcurrentQueue<MinecraftSocket.BlockedElement> topBlocked, string userId, int v)
    {
        var batch = new BatchStatement();
        var count = 0;
        var total = 0;
        while (topBlocked.Count > v)
            if (topBlocked.TryDequeue(out var blocked))
            {
                if (!ShouldArchive(blocked, DateTime.UtcNow) || !Guid.TryParse(blocked.Flip.Auction.Uuid, out var auctionUuid))
                    continue; // skip old auctions, eg from replay flips
                // Use SimpleStatement instead of table.Insert() to avoid prepared statement
                // cache eviction issues on the Cassandra server
                var statement = new SimpleStatement(InsertBlockedReasonCql,
                    userId,
                    auctionUuid,
                    (int)blocked.Flip.Finder,
                    blocked.Now,
                    blocked.Reason,
                    blocked.Flip.TargetPrice,
                    blocked.Flip.Auction.StartingBid,
                    60 * 60 * 24 * 7 // TTL in seconds
                );
                batch.Add(statement);
                batch.SetRoutingKey(statement.RoutingKey);
                total++;
                if (count++ > 20)
                {
                    await session.ExecuteAsync(batch);
                    batch = new BatchStatement();
                    count = 0;
                    Activity.Current?.Log("Archived 20 blocked flips");
                }
            }
        if (count > 0)
            await session.ExecuteAsync(batch);
        if (total > 0)
            logger.LogInformation("Archived {Count} blocked flips for {UserId}", total, userId);
    }

    public class BlockedReason
    {
        public string Reason { get; set; }
        public string UserId { get; set; }
        public Guid AuctionUuid { get; set; }
        public DateTime BlockedAt { get; set; }
        public LowPricedAuction.FinderType FinderType { get; set; }
        /// <summary>
        /// Target price of the blocked flip, 0 for rows written before the column existed
        /// </summary>
        public long TargetPrice { get; set; }
        /// <summary>
        /// Starting bid of the blocked auction, 0 for rows written before the column existed
        /// </summary>
        public long StartingBid { get; set; }
    }

    /// <summary>
    /// Groups a blocked reason into a category: the reasons embed the matched filter or finder name
    /// </summary>
    public static string GetReasonGroup(string reason)
    {
        if (string.IsNullOrEmpty(reason))
            return "unknown";
        if (reason.StartsWith("forced blacklist", StringComparison.Ordinal))
            return "forced blacklist";
        if (reason.StartsWith("blacklist", StringComparison.Ordinal))
            return "blacklist";
        if (reason.StartsWith("whitelist", StringComparison.Ordinal))
            return "whitelist";
        if (reason.StartsWith("finder ", StringComparison.Ordinal))
            return "finder";
        if (reason.StartsWith("Error ", StringComparison.Ordinal))
            return "error";
        return reason;
    }

    /// <summary>
    /// Aggregates blocked rows by reason group, most expensive group (sum of target - starting bid) first
    /// </summary>
    public static List<BlockedReasonSummary> Summarize(IEnumerable<BlockedReason> rows)
    {
        return (rows ?? []).GroupBy(r => GetReasonGroup(r.Reason)).Select(g =>
        {
            var profits = g.Select(r => r.TargetPrice - r.StartingBid).ToList();
            return new BlockedReasonSummary
            {
                ReasonGroup = g.Key,
                Count = g.Count(),
                EstimatedProfitSum = profits.Sum(),
                MaxEstimatedProfit = profits.Max(),
                ExampleAuctionUuids = g.OrderByDescending(r => r.TargetPrice - r.StartingBid).Select(r => r.AuctionUuid).Distinct().Take(3).ToList(),
                ByFinder = g.GroupBy(r => r.FinderType).ToDictionary(f => f.Key, f => f.Count()),
                Reasons = g.Select(r => r.Reason).Distinct().Take(10).ToList()
            };
        }).OrderByDescending(s => s.EstimatedProfitSum).ThenByDescending(s => s.Count).ToList();
    }

    public class BlockedReasonSummary
    {
        [Newtonsoft.Json.JsonProperty("reasonGroup")]
        public string ReasonGroup { get; set; }
        [Newtonsoft.Json.JsonProperty("count")]
        public int Count { get; set; }
        /// <summary>Sum of targetPrice - startingBid over all rows, not fee adjusted</summary>
        [Newtonsoft.Json.JsonProperty("estimatedProfitSum")]
        public long EstimatedProfitSum { get; set; }
        [Newtonsoft.Json.JsonProperty("maxEstimatedProfit")]
        public long MaxEstimatedProfit { get; set; }
        [Newtonsoft.Json.JsonProperty("exampleAuctionUuids")]
        public List<Guid> ExampleAuctionUuids { get; set; }
        // newtonsoft writes enum dictionary keys by name
        [Newtonsoft.Json.JsonProperty("byFinder")]
        public Dictionary<LowPricedAuction.FinderType, int> ByFinder { get; set; }
        /// <summary>Up to 10 distinct full reason strings of this group</summary>
        [Newtonsoft.Json.JsonProperty("reasons")]
        public List<string> Reasons { get; set; }
    }

}