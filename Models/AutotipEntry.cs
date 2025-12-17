using System;
using Cassandra.Mapping.Attributes;

namespace Coflnet.Sky.ModCommands.Models;

/// <summary>
/// Represents a completed autotip in the database
/// Supports both historical tracking and daily duplicate prevention
/// </summary>
[Table("autotip_entries")]
public class AutotipEntry
{
    /// <summary>
    /// The user ID who sent the tip
    /// </summary>
    [PartitionKey]
    [Column("user_id")]
    public string UserId { get; set; }

    /// <summary>
    /// The gamemode in which the tip was sent (arcade, skywars, tntgames, legacy)
    /// </summary>
    [ClusteringKey(0)]
    [Column("gamemode")]
    public string Gamemode { get; set; }

    /// <summary>
    /// When the tip was sent (descending order for most recent first)
    /// </summary>
    [ClusteringKey(1)]
    [Column("tipped_at")]
    public DateTimeOffset TippedAt { get; set; }

    /// <summary>
    /// The player UUID who received the tip
    /// </summary>
    [Column("tipped_player_uuid")]
    public string TippedPlayerUuid { get; set; }

    /// <summary>
    /// The player name who received the tip (for display purposes)
    /// </summary>
    [Column("tipped_player_name")]
    public string TippedPlayerName { get; set; }

    /// <summary>
    /// The amount tipped in coins
    /// </summary>
    [Column("amount")]
    public long Amount { get; set; }

    /// <summary>
    /// Whether this was an automatic tip or manual tip
    /// </summary>
    [Column("is_automatic")]
    public bool IsAutomatic { get; set; }
}

/// <summary>
/// Secondary table to track recent tips by gamemode for efficient querying
/// </summary>
[Table("autotip_recent_by_gamemode")]
public class AutotipRecentEntry
{
    /// <summary>
    /// The gamemode (partition key for efficient querying by gamemode)
    /// </summary>
    [PartitionKey]
    [Column("gamemode")]
    public string Gamemode { get; set; }

    /// <summary>
    /// The user ID who sent the tip
    /// </summary>
    [ClusteringKey(0)]
    [Column("user_id")]
    public string UserId { get; set; }

    /// <summary>
    /// When the tip was sent (descending order for most recent first)
    /// </summary>
    [ClusteringKey(1)]
    [Column("tipped_at")]
    public DateTimeOffset TippedAt { get; set; }

    /// <summary>
    /// The player UUID who received the tip
    /// </summary>
    [Column("tipped_player_uuid")]
    public string TippedPlayerUuid { get; set; }

    /// <summary>
    /// The player name who received the tip (for display purposes)
    /// </summary>
    [Column("tipped_player_name")]
    public string TippedPlayerName { get; set; }

    /// <summary>
    /// The amount tipped in coins
    /// </summary>
    [Column("amount")]
    public long Amount { get; set; }

    /// <summary>
    /// Whether this was an automatic tip or manual tip
    /// </summary>
    [Column("is_automatic")]
    public bool IsAutomatic { get; set; }
}