using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;

namespace Coflnet.Sky.ModCommands.Services;

public class ConfigStatsService
{
    public class ConfigLoad
    {
        public string ConfigName;
        public string OwnerId;
        public string UserId;
        public string McUuid;
        public DateTime LoadTime;
        public int Version;
    }
    private Table<ConfigLoad> Table;

    public ConfigStatsService(ISession session)
    {
        Table = new Table<ConfigLoad>(session, new MappingConfiguration().Define(
            new Map<ConfigLoad>().PartitionKey("owner_id", "config_name")
                .ClusteringKey(c => c.McUuid)
                .Column(x => x.ConfigName, c => c.WithName("config_name"))
                .Column(x => x.OwnerId, c => c.WithName("owner_id"))
                .Column(x => x.McUuid, c => c.WithName("mc_uuid"))
                .Column(x => x.UserId, c => c.WithName("user_id"))
                .Column(x => x.LoadTime, c => c.WithName("load_time"))
                .Column(x => x.Version, c => c.WithName("version"))
                .TableName("config_loads")
        ));
        // set ttl to 7 days
        session.Execute("CREATE TABLE IF NOT EXISTS config_loads (owner_id text, config_name text, mc_uuid text, user_id text, load_time timestamp, version int,"
            + " PRIMARY KEY ((owner_id, config_name), mc_uuid)) WITH default_time_to_live = 604800");
    }

    public async Task AddLoad(string owner, string configName, string mcUuid, string userId, int version)
    {
        await Table.Insert(new ConfigLoad()
        {
            ConfigName = configName,
            OwnerId = owner,
            McUuid = mcUuid,
            UserId = userId,
            LoadTime = DateTime.UtcNow,
            Version = version
        }).ExecuteAsync();
    }

    public async Task<IEnumerable<ConfigLoad>> GetLoads(string owner, string configName)
    {
        return await Table.Where(x => x.OwnerId == owner && x.ConfigName == configName).ExecuteAsync();
    }
}
