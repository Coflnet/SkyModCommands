using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Core;
using static Coflnet.Sky.ModCommands.Services.BlockedService;

namespace Coflnet.Sky.ModCommands.Services;

public interface IBlockedService
{
    Task AddBlockedReason(BlockedReason reason);
    Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, Guid auctionUuid);
}

public class BlockedService : IBlockedService
{
    Table<BlockedReason> table;

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
    }

    public async Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, Guid auctionUuid)
    {
        return await table.Where(x => x.UserId == userId && x.AuctionUuid == auctionUuid).ExecuteAsync();
    }

    public async Task AddBlockedReason(BlockedReason reason)
    {
        var insert = table.Insert(reason);
        insert.SetTTL(60 * 60 * 24 * 7); // 1 week
        await insert.ExecuteAsync();
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