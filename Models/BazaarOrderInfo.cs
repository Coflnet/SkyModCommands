using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Models;

public class BazaarOrderInfo
{
    public string ItemTag { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public BazaarOrderSide Side { get; set; }
    public long Amount { get; set; }
    public double PricePerUnit { get; set; }
    public long FilledAmount { get; set; }
    public bool IsExpired { get; set; }
    public string ExpirationText { get; set; } = string.Empty;
    public string PlacedBy { get; set; } = string.Empty;
    public List<BazaarOrderPlayerInfo> Players { get; set; } = new();
    public string Lore { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsSell => Side == BazaarOrderSide.Sell;

    [JsonIgnore]
    public long RemainingAmount => Math.Max(Amount - FilledAmount, 0);
}

public class BazaarOrderPlayerInfo
{
    public string PlayerName { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string RelativeTime { get; set; } = string.Empty;
}

public enum BazaarOrderSide
{
    Unknown = 0,
    Buy = 1,
    Sell = 2
}