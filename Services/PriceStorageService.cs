using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.Shared;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

public interface IPriceStorageService
{
    Task<long> GetPrice(Guid playerUuid, Guid uuid);
    Task SetPrice(Guid playerUuid, Guid uuid, long value);
}

public class PriceStorageService : IPriceStorageService
{
    private Table<PriceEstimateValue> table;
    private ILogger<PriceStorageService> logger;
    public class PriceEstimateValue
    {
        public Guid PlayerUuid { get; set; }
        public Guid Uuid { get; set; }
        public long Value { get; set; }

    }

    public PriceStorageService(ISession session, ILogger<PriceStorageService> logger)
    {
        table = new Table<PriceEstimateValue>(session, new MappingConfiguration().Define(
            new Map<PriceEstimateValue>()
                .PartitionKey(x => x.Uuid)
                .ClusteringKey(x => x.PlayerUuid)
                .Column(x => x.Value, cm => cm.WithName("value"))
                .Column(x => x.Uuid, cm => cm.WithName("uuid"))
        ), "mod_price_estimate");
        table.CreateIfNotExists();
        this.logger = logger;
        try
        {
            var res = table.Where(x => x.Uuid == Guid.Empty && x.PlayerUuid == Guid.Empty).Select(x => x.Value).FirstOrDefault().Execute();
            if (res == 0)
                throw new Exception("Empty");
        }
        catch (System.Exception e)
        {
            logger.LogError(e, "Error creating table, recreating");
            session.Execute("DROP TABLE mod_price_estimate");
            table.CreateIfNotExists();
            // insert new 
            table.Insert(new PriceEstimateValue() { Uuid = Guid.Empty, PlayerUuid = Guid.Empty, Value = 1 }).ExecuteAsync();
        }
    }

    public async Task<long> GetPrice(Guid playerUuid, Guid uuid)
    {
        try
        {

            var value = await table.Where(x => x.Uuid == uuid && x.PlayerUuid == playerUuid)
                .FirstOrDefault().ExecuteAsync();
            return value?.Value ?? 0;
        }
        catch (System.Exception e)
        {
            logger.LogError(e, "Error getting price for {uuid} {playerUuid}", uuid, playerUuid);
            return 0;
        }
    }

    public async Task SetPrice(Guid playerUuid, Guid uuid, long value)
    {
        var storageTime = 48 * 60 * 60;
        if (value < 0)
            storageTime *= 7;
        var exiting = await table.Where(x => x.Uuid == uuid && x.PlayerUuid == playerUuid)
            .FirstOrDefault().ExecuteAsync();
        if (exiting.Value > value)
        {
            logger.LogInformation($"Price for {uuid} is already higher than {value}, not updating for {playerUuid}");
            return;
        }
        var insert = table.Insert(new PriceEstimateValue() { Uuid = uuid, PlayerUuid = playerUuid, Value = value });
        // insert with ttl 48h
        insert.SetTTL(storageTime);
        // move higher estimations later to override lower "earlier" ones
        insert.SetTimestamp(DateTime.UtcNow.AddSeconds(value / 5_000_000));
        await insert.ExecuteAsync();
        Activity.Current?.Log($"Set price for {uuid} to {value}");
    }
}
