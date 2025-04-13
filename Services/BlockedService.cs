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
using static Coflnet.Sky.ModCommands.Services.BlockedService;

namespace Coflnet.Sky.ModCommands.Services;

public interface IBlockedService
{
    Task ArchiveBlockedFlipsUntil(ConcurrentQueue<MinecraftSocket.BlockedElement> topBlocked, string userId, int v);
    Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, Guid auctionUuid);
}

public class BlockedService : IBlockedService
{
    Table<BlockedReason> table;
    ISession session;

    public BlockedService(ISession session)
    {
        var mapping = new MappingConfiguration().Define(
            new Map<BlockedReason>()
                .TableName("blocked_reasons")
                .PartitionKey(x => x.UserId)
                .ClusteringKey(x => x.AuctionUuid)
                .ClusteringKey(x => x.FinderType)
                .Column(o => o.FinderType, c => c.WithName("finder_type").WithDbType<int>())
                .Column(x => x.BlockedAt, cm => cm.WithName("blocked_at"))
                .Column(x => x.Reason, cm => cm.WithName("reason"))
        );
        table = new Table<BlockedReason>(session, mapping);
        table.CreateIfNotExists();
        this.session = session;
    }

    public async Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, Guid auctionUuid)
    {
        return await table.Where(x => x.UserId == userId && x.AuctionUuid == auctionUuid).ExecuteAsync();
    }

    public async Task ArchiveBlockedFlipsUntil(ConcurrentQueue<MinecraftSocket.BlockedElement> topBlocked, string userId, int v)
    {
        var batch = new BatchStatement();
        var count = 0;
        while (topBlocked.Count > v)
            if (topBlocked.TryDequeue(out var blocked))
            {
                if(blocked.Flip.Auction.Start < DateTime.UtcNow.AddHours(2))
                    continue; // skip old auctions, eg from replay flips
                var statement = table.Insert(new()
                {
                    AuctionUuid = Guid.Parse(blocked.Flip.Auction.Uuid),
                    BlockedAt = blocked.Now,
                    FinderType = blocked.Flip.Finder,
                    Reason = blocked.Reason,
                    UserId = userId
                }).SetTTL(60 * 60 * 24 * 7);
                batch.Add(statement);
                batch.SetRoutingKey(statement.RoutingKey);
                if (count++ > 20)
                {
                    await session.ExecuteAsync(batch);
                    batch = new BatchStatement();
                    count = 0;
                    Activity.Current.Log("Archived 20 blocked flips");
                }
            }
        await session.ExecuteAsync(batch);
    }

    public class BlockedReason
    {
        public string Reason { get; set; }
        public string UserId { get; set; }
        public Guid AuctionUuid { get; set; }
        public DateTime BlockedAt { get; set; }
        public LowPricedAuction.FinderType FinderType { get; set; }
    }
}