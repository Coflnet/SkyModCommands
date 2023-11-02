using Cassandra.Mapping.Attributes;

namespace Coflnet.Sky.ModCommands.Services;

public class CountTable
{
    [PartitionKey]
    public string Id { get; set; }
    [ClusteringKey(0)]
    public string Name { get; set; }
    [Counter()]
    public long Value { get; set; }
}
