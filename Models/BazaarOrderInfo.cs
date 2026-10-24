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

public class SentBazaarOrderInfo
{
    public string ItemTag { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public BazaarOrderSide Side { get; set; }
    public double PricePerUnit { get; set; }
    public long Amount { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>Plan intent this order was sent for (null for legacy tier-slice recommendations).</summary>
    public string Mode { get; set; }
    public string QuoteId { get; set; }
    public int? OriginalQuantity { get; set; }
    public double? HorizonHours { get; set; }
    /// <summary>Planned exit price; sells of this tag are never recommended below it.</summary>
    public double? ExitPrice { get; set; }
}

/// <summary>Planner metadata attached to a sent BUY recommendation.</summary>
public record BazaarOrderPlanIntent(string Mode, string QuoteId, int OriginalQuantity, double HorizonHours, double? ExitPrice);

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
