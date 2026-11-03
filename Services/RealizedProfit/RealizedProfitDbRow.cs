using System;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// Stored shape of a <see cref="RealizedProfitRow"/>: no null cells (they would become tombstones),
/// a failed source is stored as 0 plus a bit in <see cref="Missing"/>. Ints only, the cassandra driver cannot map enums or bytes.
/// </summary>
public class RealizedProfitDbRow
{
    public const int AhMissing = 1;
    public const int BazaarMissing = 2;
    public const int TaskMissing = 4;

    public int Tier { get; set; }
    /// <summary>UTC date of <see cref="UpdatedAt"/> as yyyymmdd</summary>
    public int Day { get; set; }
    public string UserId { get; set; } = "";
    public long AhProfit { get; set; }
    public long BazaarProfit { get; set; }
    public long TaskValue { get; set; }
    public int Missing { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static int DayOf(DateTime utc) => utc.Year * 10000 + utc.Month * 100 + utc.Day;

    public static RealizedProfitDbRow From(RealizedProfitRow row)
    {
        var updated = row.UpdatedAt.ToUniversalTime();
        return new RealizedProfitDbRow
        {
            Tier = row.Tier,
            Day = DayOf(updated),
            UserId = row.UserId,
            AhProfit = row.AhProfit ?? 0,
            BazaarProfit = row.BazaarProfit ?? 0,
            TaskValue = row.TaskValue ?? 0,
            Missing = (row.AhProfit == null ? AhMissing : 0) | (row.BazaarProfit == null ? BazaarMissing : 0) | (row.TaskValue == null ? TaskMissing : 0),
            UpdatedAt = updated
        };
    }

    public RealizedProfitRow ToRow() => new()
    {
        Tier = Tier,
        UserId = UserId,
        AhProfit = (Missing & AhMissing) != 0 ? null : AhProfit,
        BazaarProfit = (Missing & BazaarMissing) != 0 ? null : BazaarProfit,
        TaskValue = (Missing & TaskMissing) != 0 ? null : TaskValue,
        UpdatedAt = DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)
    };
}
