using System;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;

namespace Coflnet.Sky.ModCommands.Services;

public class PriceStorageService
{
    private Table<PriceEstimateValue> table;
    public class PriceEstimateValue
    {
        public Guid PlayerUuid { get; set; }
        public Guid Uuid { get; set; }
        public long Value { get; set; }

    }

    public PriceStorageService(ISession session)
    {
        table = new Table<PriceEstimateValue>(session, new MappingConfiguration().Define(
            new Map<PriceEstimateValue>()
                .PartitionKey(x => x.Uuid)
                .ClusteringKey(x => x.PlayerUuid)
                .Column(x => x.Value, cm => cm.WithName("value"))
                .Column(x => x.Uuid, cm => cm.WithName("uuid"))
        ), "mod_price_estimate");
        table.CreateIfNotExists();
    }

    public async Task<long> GetPrice(Guid uuid, Guid playerUuid)
    {
        return await table.Where(x => x.Uuid == uuid && x.PlayerUuid == playerUuid)
            .Select(x => x.Value)
            .FirstOrDefault().ExecuteAsync();
    }

    public async Task SetPrice(Guid playerUuid, Guid uuid, long value)
    {
        // insert with ttl 48h
        await table.Insert(new PriceEstimateValue() { Uuid = uuid, PlayerUuid = playerUuid, Value = value }).SetTTL(48 * 60 * 60).ExecuteAsync();
    }
}
