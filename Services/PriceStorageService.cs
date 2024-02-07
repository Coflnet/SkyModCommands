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
        public Guid Uuid { get; set; }
        public long Value { get; set; }

    }

    public PriceStorageService(ISession session)
    {
        table = new Table<PriceEstimateValue>(session, new MappingConfiguration().Define(
            new Map<PriceEstimateValue>()
                .TableName("mod_price_estimate")
                .PartitionKey(x => x.Uuid)
                .Column(x => x.Value, cm => cm.WithName("value"))
                .Column(x => x.Uuid, cm => cm.WithName("uuid"))
        ));
    }

    public async Task<long> GetPrice(Guid uuid)
    {
        return await table.Where(x => x.Uuid == uuid)
            .Select(x => x.Value)
            .FirstOrDefault().ExecuteAsync();
    }

    public async Task SetPrice(Guid uuid, long value)
    {
        // insert with ttl 48h
        await table.Insert(new PriceEstimateValue() { Uuid = uuid, Value = value }).SetTTL(48 * 60 * 60).ExecuteAsync();
    }
}
