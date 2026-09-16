using System;
using System.Collections.Generic;

namespace Coflnet.Sky.ModCommands.Models;

// Published exclusively by SkyBazaar after it applies a chat, description or market observation.
public class BazaarOrderSnapshot
{
    public const string Channel = "bazaar:orders:v1";
    public string UserId { get; set; }
    public string PlayerName { get; set; }
    public long Revision { get; set; }
    public string TraceParent { get; set; }
    public string TraceState { get; set; }
    public bool Created { get; set; }
    public List<BazaarDisplayOrder> Orders { get; set; } = new();
    public Dictionary<string, string> ItemNames { get; set; } = new();
}

public class BazaarDisplayOrder
{
    public string ItemId { get; set; }
    public string PlayerName { get; set; }
    public int Amount { get; set; }
    public int Filled { get; set; }
    public bool IsExpired { get; set; }
    public int? Claimed { get; set; }
    public bool? IsEstimate { get; set; }
    public bool IsSell { get; set; }
    public double PricePerUnit { get; set; }
    public DateTime Timestamp { get; set; }
}
