using System;
using System.Collections.Generic;
using Cassandra.Mapping.Attributes;

namespace Coflnet.Sky.ModCommands.Models;

[Table("lowball_offers")]
public class LowballOffer
{
    [PartitionKey]
    [Column("user_id")]
    public string UserId { get; set; }

    [ClusteringKey(0)]
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [ClusteringKey(1)]
    [Column("offer_id")]
    public Guid OfferId { get; set; }

    [Column("item_tag")]
    public string ItemTag { get; set; }

    [Column("minecraft_account")]
    public Guid MinecraftAccount { get; set; }

    [Column("item_name")]
    public string ItemName { get; set; }

    [Column("api_auction_json")]
    public string ApiAuctionJson { get; set; }

    [Column("filters")]
    public string Filters { get; set; }

    [Column("asking_price")]
    public long AskingPrice { get; set; }

    [Column("lore")]
    public string Lore { get; set; }

    [Column("item_count")]
    public int ItemCount { get; set; }
}

[Table("lowball_offers_by_item")]
public class LowballOfferByItem
{
    [PartitionKey]
    [Column("item_tag")]
    public string ItemTag { get; set; }

    [ClusteringKey(0)]
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [ClusteringKey(1)]
    [Column("offer_id")]
    public Guid OfferId { get; set; }

    [Column("user_id")]
    public string UserId { get; set; }

    [Column("minecraft_account")]
    public Guid MinecraftAccount { get; set; }

    [Column("item_name")]
    public string ItemName { get; set; }

    [Column("api_auction_json")]
    public string ApiAuctionJson { get; set; }

    [Column("filters")]
    public string Filters { get; set; }

    [Column("asking_price")]
    public long AskingPrice { get; set; }

    [Column("lore")]
    public string Lore { get; set; }

    [Column("item_count")]
    public int ItemCount { get; set; }
}
