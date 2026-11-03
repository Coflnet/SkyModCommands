using System;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// One user of one tier, a null source means it could not be loaded.
/// Tier is an int because the cassandra driver cannot map enums.
/// </summary>
public class RealizedProfitRow
{
    public int Tier { get; set; }
    public string UserId { get; set; } = "";
    public long? AhProfit { get; set; }
    public long? BazaarProfit { get; set; }
    public long? TaskValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}
