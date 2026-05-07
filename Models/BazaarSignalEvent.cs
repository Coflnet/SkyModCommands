using System;

namespace Coflnet.Sky.ModCommands.Models;
#nullable enable

public static class BazaarSignalChannels
{
    public const string LiveSignals = "bazaar:signals:v1";
}

public static class BazaarSignalTypes
{
    public const string OrderFilled = "order_filled";
    public const string InstaSellIntent = "insta_sell_intent";
}

public static class BazaarSignalSides
{
    public const string BuyOrder = "buy_order";
    public const string SellOffer = "sell_offer";
}

public class BazaarSignalEvent
{
    public string Type { get; set; } = string.Empty;
    public string ItemTag { get; set; } = string.Empty;
    public string? ItemName { get; set; }
    public string? UserId { get; set; }
    public string? MinecraftUuid { get; set; }
    public string? MinecraftName { get; set; }
    public string? OrderSide { get; set; }
    public int Amount { get; set; }
    public int InventoryAmount { get; set; }
    public double? PricePerUnit { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Source { get; set; }
}