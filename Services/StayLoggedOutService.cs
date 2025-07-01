using System;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

public class StayLoggedOutService
{
    private Table<StayLoggedOut> table;
    private ILogger<StayLoggedOutService> logger;

    public StayLoggedOutService(ISession session, ILogger<StayLoggedOutService> logger)
    {
        table = new Table<StayLoggedOut>(session, new MappingConfiguration().Define(
            new Map<StayLoggedOut>()
                .PartitionKey(x => x.PlayerUuid)
                .Column(x => x.Until, cm => cm.WithName("until"))
        ), "mod_stay_logged_out");
        table.CreateIfNotExists();
        this.logger = logger;
    }

    public async Task<bool> WantsToBeLoggedout(string connectionId)
    {
        try
        {
            var res = await table.Where(x => x.PlayerUuid == connectionId)
                .Select(x => x.Until).FirstOrDefault().ExecuteAsync();
            return res > DateTime.UtcNow;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error checking if player {playerUuid} is logged out", connectionId);
            return false;
        }
    }

    public async Task SetLoggedOut(string connectionId, DateTime until)
    {
        try
        {
            await table.Insert(new StayLoggedOut { PlayerUuid = connectionId, Until = until }).ExecuteAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error setting logged out for player {playerUuid}", connectionId);
        }
    }


    public class StayLoggedOut
    {
        public string PlayerUuid { get; set; }
        public DateTime Until { get; set; }
    }
}
