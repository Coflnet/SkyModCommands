using System;
using System.Collections.Generic;
using System.Globalization;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

#nullable enable

namespace Coflnet.Sky.ModCommands.Services.Donut;

public static class DonutPublishedFlipParser
{
    public static LowPricedAuction? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        PublishedDonutFlip? publishedFlip;
        try
        {
            publishedFlip = JsonConvert.DeserializeObject<PublishedDonutFlip>(payload);
        }
        catch
        {
            return null;
        }

        return Convert(publishedFlip);
    }

    private static LowPricedAuction? Convert(PublishedDonutFlip? publishedFlip)
    {
        if (publishedFlip == null)
            return null;

        var auction = TryDeserialize<PublishedDonutAuction>(publishedFlip.SerializedAuction);
        var seller = auction?.Seller ?? publishedFlip.Seller;
        var item = auction?.Item;
        var itemId = NormalizeDonutItemId(item?.Id ?? publishedFlip.ItemId ?? publishedFlip.Id ?? publishedFlip.ItemKey);
        var itemCount = Math.Max(1, item?.Count ?? 1);
        var startingBid = ToLong(auction?.Price ?? publishedFlip.AuctionPrice);
        if (startingBid <= 0)
            return null;

        var targetPrice = publishedFlip.MedianPrice > 0
            ? ToLong(publishedFlip.MedianPrice * itemCount)
            : Math.Max(startingBid, startingBid + publishedFlip.ProfitAmount);
        var foundAt = publishedFlip.FoundAt == default ? DateTime.UtcNow : publishedFlip.FoundAt;
        var auctionId = !string.IsNullOrWhiteSpace(publishedFlip.AuctionId)
            ? publishedFlip.AuctionId
            : $"{seller?.Uuid ?? DonutServerContext.Name}:{publishedFlip.ItemKey ?? itemId}:{startingBid}";
        var context = new Dictionary<string, string>
        {
            ["server"] = DonutServerContext.Name,
            ["source"] = "donut:flips",
            ["donutItemKey"] = publishedFlip.ItemKey ?? itemId
        };
        if (!string.IsNullOrWhiteSpace(seller?.Name))
            context["sellerName"] = seller.Name;
        if (publishedFlip.ReferenceAuctionCount > 0)
            context["referenceAuctionCount"] = publishedFlip.ReferenceAuctionCount.ToString(CultureInfo.InvariantCulture);
        if (publishedFlip.ShulkerItemCount > 0)
            context["shulkerItemCount"] = publishedFlip.ShulkerItemCount.ToString(CultureInfo.InvariantCulture);
        if (publishedFlip.PercentBelow != 0)
            context["percentBelow"] = publishedFlip.PercentBelow.ToString(CultureInfo.InvariantCulture);
        if (item?.Lore?.Count > 0)
            context["lore"] = string.Join("\n", item.Lore);

        return new LowPricedAuction
        {
            Auction = new SaveAuction
            {
                StartingBid = startingBid,
                HighestBidAmount = startingBid,
                ItemName = item?.DisplayName ?? publishedFlip.DisplayName ?? itemId,
                AuctioneerId = seller?.Uuid ?? DonutServerContext.Name,
                Uuid = auctionId,
                Bin = true,
                UId = AuctionService.Instance.GetId(auctionId),
                Context = context,
                FindTime = foundAt,
                Start = foundAt,
                End = foundAt + TimeSpan.FromSeconds(Math.Max(auction?.TimeLeft ?? 30, 30)),
                Bids = new List<SaveBids>(),
                Tag = itemId
            },
            DailyVolume = Math.Max(1, publishedFlip.ReferenceAuctionCount),
            Finder = LowPricedAuction.FinderType.FLIPPER_AND_SNIPERS,
            TargetPrice = Math.Max(targetPrice, startingBid),
            AdditionalProps = new Dictionary<string, string>
            {
                ["server"] = DonutServerContext.Name,
                ["source"] = "donut:flips",
                ["donutItemKey"] = publishedFlip.ItemKey ?? itemId
            }
        };
    }

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeDonutItemId(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return "unknown";
        return itemId.Replace("minecraft:", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static long ToLong(decimal value)
    {
        if (value > long.MaxValue)
            return long.MaxValue;
        if (value < long.MinValue)
            return long.MinValue;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private sealed class PublishedDonutFlip
    {
        public string? AuctionId { get; set; }
        public string? ItemKey { get; set; }
        public string? ItemId { get; set; }
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public decimal AuctionPrice { get; set; }
        public decimal MedianPrice { get; set; }
        public long ProfitAmount { get; set; }
        public decimal PercentBelow { get; set; }
        public PublishedDonutSeller? Seller { get; set; }
        public DateTime FoundAt { get; set; }
        public int ReferenceAuctionCount { get; set; }
        public int ShulkerItemCount { get; set; }
        public string? SerializedAuction { get; set; }
    }

    private sealed class PublishedDonutAuction
    {
        [JsonProperty("item")]
        public PublishedDonutItem? Item { get; set; }

        [JsonProperty("price")]
        public decimal Price { get; set; }

        [JsonProperty("seller")]
        public PublishedDonutSeller? Seller { get; set; }

        [JsonProperty("time_left")]
        public int TimeLeft { get; set; }
    }

    private sealed class PublishedDonutItem
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("display_name")]
        public string? DisplayName { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; } = 1;

        [JsonProperty("lore")]
        public List<string>? Lore { get; set; }
    }

    private sealed class PublishedDonutSeller
    {
        [JsonProperty("uuid")]
        public string? Uuid { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }
    }
}