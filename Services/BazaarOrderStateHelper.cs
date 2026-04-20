using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Models;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.ModCommands.Services;

public static class BazaarOrderStateHelper
{
    public const int BottomMenuRows = 4;
    public const int SlotsPerRow = 9;
    public const int MaxOpenBuyOrders = 20;
    public const string CancelOrderDisplayNameColored = "§cCancel Order";
    public const string CancelOrderDisplayName = "Cancel Order";

    private static readonly Regex FormattingRegex = new("§.", RegexOptions.Compiled);
    private static readonly Regex AmountRegex = new(@"(?:Offer amount|Order amount|Selling|Order): §a([\d,]+)§7x", RegexOptions.Compiled);
    private static readonly Regex PriceRegex = new(@"Price per unit: §6([\d,]+(?:\.\d+)?) coins", RegexOptions.Compiled);
    private static readonly Regex FilledRegex = new(@"Filled: §6([\d,]+)§7/(?:§6)?([\d,]+)", RegexOptions.Compiled);
    private static readonly Regex PlayerRegex = new(@"^§8- §a([\d,]+)§7x (.+?)(?:§f §8(.+))?$", RegexOptions.Compiled);
    private static readonly Regex ByRegex = new(@"^§7By: (.+)$", RegexOptions.Compiled);
    private static readonly Regex ExpiresRegex = new(@"^(?:Expires in|Expiration): (.+)$", RegexOptions.Compiled);

    public static bool IsOrderOptionsSnapshot(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        var root = JToken.Parse(arguments);
        if (root["slots"] is not JArray slots)
            return false;

        return slots
            .OfType<JObject>()
            .Any(slot => slot["slot"]?.Value<int>() == 13
                && (slot["displayNameColored"]?.ToString() == CancelOrderDisplayNameColored
                    || slot["displayName"]?.ToString() == CancelOrderDisplayName));
    }

    public static List<BazaarOrderInfo> ParseOpenOrders(string arguments, InventoryParser parser)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return [];

        var root = JToken.Parse(arguments);
        var rawSlots = root["slots"] as JArray;
        var parsed = parser.Parse(arguments).ToList();
        var trackedSlotCount = GetTrackedSlotCount(arguments, parsed.Count);

        if (rawSlots != null)
        {
            return Enumerable.Range(0, Math.Min(trackedSlotCount, rawSlots.Count))
                .Select(index => ParseOrder(rawSlots[index], index < parsed.Count ? parsed[index] : null))
                .Where(IsTrackedOrder)
                .ToList();
        }

        return parsed
            .Take(trackedSlotCount)
            .Select(ParseOrder)
            .Where(IsTrackedOrder)
            .ToList();
    }

    public static int GetTrackedSlotCount(string arguments, int parsedSlotCount)
    {
        if (parsedSlotCount <= 0)
            return 0;

        var slotCount = parsedSlotCount;
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            var root = JToken.Parse(arguments);
            if (root["slotCount"]?.Type == JTokenType.Integer)
                slotCount = root["slotCount"]!.Value<int>();
            else if (root["slots"] is JArray slots)
                slotCount = slots.Count;
        }

        var trackedSlots = slotCount - BottomMenuRows * SlotsPerRow;
        return Math.Clamp(trackedSlots, 0, parsedSlotCount);
    }

    public static bool HasReachedBuyOrderLimit(IEnumerable<BazaarOrderInfo> orders)
    {
        return orders?.Count(IsTrackedOrder) >= MaxOpenBuyOrders;
    }

    public static bool HasTrackedSentOrder(IEnumerable<SentBazaarOrderInfo> sentOrders, string itemTag, string itemName, BazaarOrderSide side, double pricePerUnit)
    {
        if (sentOrders == null)
            return false;

        var key = GetTrackingKey(itemTag, itemName, side, pricePerUnit);
        return sentOrders.Any(order => GetTrackingKey(order) == key);
    }

    public static bool TryTrackSentOrder(List<SentBazaarOrderInfo> sentOrders, string itemTag, string itemName, BazaarOrderSide side, double pricePerUnit, long amount)
    {
        if (sentOrders == null)
            return false;

        if (HasTrackedSentOrder(sentOrders, itemTag, itemName, side, pricePerUnit))
            return false;

        sentOrders.Add(new SentBazaarOrderInfo
        {
            ItemTag = NormalizeItemTag(itemTag),
            ItemName = StripFormatting(itemName).Trim(),
            Side = side,
            PricePerUnit = pricePerUnit,
            Amount = amount,
            SentAt = DateTime.UtcNow
        });
        return true;
    }

    public static void SyncSentOrdersWithUpload(List<SentBazaarOrderInfo> sentOrders, IEnumerable<BazaarOrderInfo> uploadedOrders)
    {
        if (sentOrders == null || sentOrders.Count == 0)
            return;

        var uploadedKeys = (uploadedOrders ?? Enumerable.Empty<BazaarOrderInfo>())
            .Where(IsTrackedOrder)
            .Select(GetTrackingKey)
            .ToHashSet();

        foreach (var tracked in sentOrders.Where(order => uploadedKeys.Contains(GetTrackingKey(order))))
        {
            tracked.ConfirmedAt ??= DateTime.UtcNow;
        }

        sentOrders.RemoveAll(order => !uploadedKeys.Contains(GetTrackingKey(order)));
    }

    private static BazaarOrderInfo ParseOrder(SaveAuction item)
    {
        if (item == null)
            return null;

        return ParseOrder(
            NormalizeItemTag(item.Tag),
            item.ItemName ?? string.Empty,
            item.Context?.GetValueOrDefault("lore") ?? string.Empty);
    }

    private static BazaarOrderInfo ParseOrder(JToken rawItem, SaveAuction parsedItem)
    {
        if (rawItem == null || rawItem.Type == JTokenType.Null)
            return ParseOrder(parsedItem);

        if (rawItem["empty"]?.Value<bool>() == true)
            return null;

        var displayName = rawItem["displayNameColored"]?.ToString();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = rawItem["displayName"]?.ToString();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = parsedItem?.ItemName ?? string.Empty;

        var lore = rawItem["lore"] is JArray loreArray
            ? string.Join("\n", loreArray.Select(line => line?.ToString() ?? string.Empty))
            : parsedItem?.Context?.GetValueOrDefault("lore") ?? string.Empty;

        var itemTag = rawItem["tag"]?.ToString();
        if (string.IsNullOrWhiteSpace(itemTag))
            itemTag = NormalizeItemTag(parsedItem?.Tag);

        return ParseOrder(itemTag, displayName, lore);
    }

    private static BazaarOrderInfo ParseOrder(string itemTag, string displayName, string lore)
    {
        displayName ??= string.Empty;
        lore ??= string.Empty;

        var lines = lore.Split('\n', StringSplitOptions.None);
        var plainDisplayName = StripFormatting(displayName);
        var amountMatch = lines.Select(line => AmountRegex.Match(line)).FirstOrDefault(match => match.Success);
        var filledMatch = lines.Select(line => FilledRegex.Match(line)).FirstOrDefault(match => match.Success);
        var players = lines
            .Select(ParsePlayer)
            .Where(player => player != null)
            .ToList();
        var priceMatch = lines.Select(line => PriceRegex.Match(line)).FirstOrDefault(match => match.Success);
        var byMatch = lines.Select(line => ByRegex.Match(line)).FirstOrDefault(match => match.Success);
        var expirationText = GetExpirationText(lines);

        var amount = ParseLong(amountMatch?.Groups[1].Value);
        var totalFromFilled = ParseLong(filledMatch?.Groups[2].Value);
        if (amount == 0)
            amount = totalFromFilled;

        var filledFromLore = ParseLong(filledMatch?.Groups[1].Value);
        var filledFromPlayers = players.Sum(player => player.Amount);

        return new BazaarOrderInfo
        {
            ItemTag = itemTag ?? string.Empty,
            DisplayName = displayName,
            ItemName = ExtractItemName(plainDisplayName),
            Side = ParseSide(plainDisplayName, lines),
            Amount = amount,
            PricePerUnit = ParseDouble(priceMatch?.Groups[1].Value),
            FilledAmount = Math.Max(filledFromLore, filledFromPlayers),
            IsExpired = lore.Contains("Expired!", StringComparison.OrdinalIgnoreCase),
            ExpirationText = expirationText,
            PlacedBy = byMatch?.Success == true ? byMatch.Groups[1].Value : string.Empty,
            Players = players,
            Lore = lore
        };
    }

    private static bool IsTrackedOrder(BazaarOrderInfo order)
    {
        return order != null
            && order.Side != BazaarOrderSide.Unknown
            && !string.IsNullOrWhiteSpace(order.ItemName)
            && order.Amount > 0
            && order.PricePerUnit > 0;
    }

    private static BazaarOrderPlayerInfo ParsePlayer(string line)
    {
        var match = PlayerRegex.Match(line ?? string.Empty);
        if (!match.Success)
            return null;

        return new BazaarOrderPlayerInfo
        {
            Amount = ParseLong(match.Groups[1].Value),
            PlayerName = match.Groups[2].Value,
            RelativeTime = match.Groups[3].Value
        };
    }

    private static BazaarOrderSide ParseSide(string plainDisplayName, string[] lines)
    {
        if (plainDisplayName.StartsWith("SELL ", StringComparison.OrdinalIgnoreCase)
            || lines.Any(line => line.Contains("Offer amount:", StringComparison.OrdinalIgnoreCase)))
            return BazaarOrderSide.Sell;

        if (plainDisplayName.StartsWith("BUY ", StringComparison.OrdinalIgnoreCase)
            || lines.Any(line => line.Contains("Order amount:", StringComparison.OrdinalIgnoreCase)))
            return BazaarOrderSide.Buy;

        return BazaarOrderSide.Unknown;
    }

    private static string ExtractItemName(string plainDisplayName)
    {
        if (string.IsNullOrWhiteSpace(plainDisplayName))
            return string.Empty;

        if (plainDisplayName.StartsWith("SELL ", StringComparison.OrdinalIgnoreCase))
            return plainDisplayName["SELL ".Length..].Trim();

        if (plainDisplayName.StartsWith("BUY ", StringComparison.OrdinalIgnoreCase))
            return plainDisplayName["BUY ".Length..].Trim();

        return plainDisplayName.Trim();
    }

    private static string GetExpirationText(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var stripped = StripFormatting(line).Trim();
            if (string.Equals(stripped, "Expired!", StringComparison.OrdinalIgnoreCase))
                return stripped;

            var match = ExpiresRegex.Match(stripped);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return string.Empty;
    }

    private static string StripFormatting(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : FormattingRegex.Replace(value, string.Empty);
    }

    private static long ParseLong(string value)
    {
        return long.TryParse(value?.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static double ParseDouble(string value)
    {
        return double.TryParse(value?.Replace(",", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static string NormalizeItemTag(string itemTag)
    {
        return string.Equals(itemTag, "UNKOWN", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : itemTag ?? string.Empty;
    }

    private static string GetTrackingKey(BazaarOrderInfo order)
    {
        return GetTrackingKey(order.ItemTag, order.ItemName, order.Side, order.PricePerUnit);
    }

    private static string GetTrackingKey(SentBazaarOrderInfo order)
    {
        return GetTrackingKey(order.ItemTag, order.ItemName, order.Side, order.PricePerUnit);
    }

    private static string GetTrackingKey(string itemTag, string itemName, BazaarOrderSide side, double pricePerUnit)
    {
        var normalizedTag = NormalizeItemTag(itemTag);
        var identifier = string.IsNullOrWhiteSpace(normalizedTag)
            ? StripFormatting(itemName).Trim().ToUpperInvariant()
            : normalizedTag.ToUpperInvariant();
        var normalizedPrice = (long)Math.Round(pricePerUnit * 10, MidpointRounding.AwayFromZero);
        return $"{(int)side}:{identifier}:{normalizedPrice}";
    }
}