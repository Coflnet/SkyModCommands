using Cassandra.Mapping.Attributes;

namespace Coflnet.Sky.ModCommands.Services;

[Table("proxy_counters")]
public class ProxyCounterTable
{
    [PartitionKey]
    [Column("user_id")]
    public string UserId { get; set; }

    [Counter]
    [Column("request_count")]
    public long RequestCount { get; set; }
}
