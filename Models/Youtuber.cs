using System;
using Cassandra.Mapping.Attributes;

namespace Coflnet.Sky.ModCommands.Models;

/// <summary>
/// Maps a YouTuber display name (partition key: lowercased name) to a UUID
/// Stored so the FilterStateService can filter by youtuber UUIDs.
/// </summary>
[Table("youtubers")]
public class Youtuber
{
    [PartitionKey]
    [Column("name_lower")]
    public string NameLower { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("uuid")]
    public string Uuid { get; set; }

    [Column("last_updated")]
    public DateTimeOffset LastUpdated { get; set; }
}
