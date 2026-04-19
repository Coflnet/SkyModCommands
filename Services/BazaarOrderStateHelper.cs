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

    private static readonly Regex FormattingRegex = new("§.", RegexOptions.Compiled);
    private static readonly Regex AmountRegex = new(@"(?:Offer amount|Order amount|Selling|Order): §a([\d,]+)§7x", RegexOptions.Compiled);
    private static readonly Regex PriceRegex = new(@"Price per unit: §6([\d,]+(?:\.\d+)?) coins", RegexOptions.Compiled);
    private static readonly Regex FilledRegex = new(@"Filled: §6([\d,]+)§7/(?:§6)?([\d,]+)", RegexOptions.Compiled);
    private static readonly Regex PlayerRegex = new(@"^§8- §a([\d,]+)§7x (.+?)(?:§f §8(.+))?$", RegexOptions.Compiled);
    private static readonly Regex ByRegex = new(@"^§7By: (.+)$", RegexOptions.Compiled);
    private static readonly Regex ExpiresRegex = new(@"^(?:Expires in|Expiration): (.+)$", RegexOptions.Compiled);

    public static List<BazaarOrderInfo> ParseOpenOrders(string arguments, InventoryParser parser)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return [];

        var parsed = parser.Parse(arguments).ToList();
        var trackedSlotCount = GetTrackedSlotCount(arguments, parsed.Count);

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

    private static BazaarOrderInfo ParseOrder(SaveAuction item)
    {
        if (item == null)
            return null;

        var lore = item.Context?.GetValueOrDefault("lore") ?? string.Empty;
        var lines = lore.Split('\n', StringSplitOptions.None);
        var plainDisplayName = StripFormatting(item.ItemName);
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
            ItemTag = item.Tag ?? string.Empty,
            DisplayName = item.ItemName ?? string.Empty,
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
            && !string.IsNullOrWhiteSpace(order.ItemTag)
            && !string.Equals(order.ItemTag, "UNKOWN", StringComparison.OrdinalIgnoreCase)
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
}