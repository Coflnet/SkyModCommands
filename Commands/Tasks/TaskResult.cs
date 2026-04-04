using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Classifies how a task is performed
/// </summary>
public enum TaskType
{
    /// <summary>Active tasks require continuous player attention (grinding mobs, mining, fishing)</summary>
    Active,
    /// <summary>Passive tasks only require setup then waiting (forging, kat, composter, traps)</summary>
    Passive,
    /// <summary>Limited tasks can only be done once per day or every few hours</summary>
    Limited
}

public class TaskResult
{
    public int ProfitPerHour { get; set; }
    public string Message { get; set; } = "No detailed instructions available.";
    public string Details { get; set; }
    public string OnClick { get; set; }
    /// <summary>
    /// Indicates if the task is mostly passive, meaning it can be done in parallel to others (requiring mostly waiting)
    /// </summary>
    public bool MostlyPassive { get; set; }
    /// <summary>
    /// Classification: Active (requires grinding), Passive (setup + wait), Limited (daily/cooldown)
    /// </summary>
    public TaskType Type { get; set; } = TaskType.Active;
    public string Name { get; set; }
    /// <summary>
    /// When this result was calculated (for freshness checks by API consumers)
    /// </summary>
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// Detailed breakdown for API consumers (website, external services)
    /// </summary>
    public MethodBreakdown Breakdown { get; set; }
    /// <summary>
    /// Whether this task is currently accessible (false = time-locked, mayor-locked, or already completed today)
    /// </summary>
    public bool IsAccessible { get; set; } = true;
    /// <summary>
    /// Human-readable reason why the task is not accessible (null if accessible)
    /// </summary>
    public string InaccessibleReason { get; set; }
    /// <summary>
    /// For limited tasks: when this task can next be done (null if always available)
    /// </summary>
    public DateTime? NextAvailableAt { get; set; }
}

/// <summary>
/// Full breakdown of a money-making method for API consumers
/// </summary>
public class MethodBreakdown
{
    /// <summary>
    /// Detailed explanation of what to do (step-by-step)
    /// </summary>
    public string HowTo { get; set; }
    /// <summary>
    /// Item tags required to get started (can look up price to estimate startup cost)
    /// </summary>
    public List<RequiredItem> RequiredItems { get; set; } = [];
    /// <summary>
    /// Expected item drops with rates and value contribution
    /// </summary>
    public List<DropInfo> Drops { get; set; } = [];
    /// <summary>
    /// Estimated actions per hour (kills, catches, mines, etc.)
    /// </summary>
    public double ActionsPerHour { get; set; }
    /// <summary>
    /// Name of the action unit (kills, catches, runs, mines)
    /// </summary>
    public string ActionUnit { get; set; } = "actions";
    /// <summary>
    /// Effects that can increase drop rates or speed
    /// </summary>
    public List<DropEffect> Effects { get; set; } = [];
    /// <summary>
    /// Whether the profit estimate comes from player data or formula
    /// </summary>
    public string Source { get; set; }
    /// <summary>
    /// Hours of player data used for calculation (0 if formula-based)
    /// </summary>
    public double TrackedHours { get; set; }
    /// <summary>
    /// Category for grouping (Fishing, Mining, Slayer, Dungeon, Farming, etc.)
    /// </summary>
    public string Category { get; set; }
    /// <summary>
    /// Bonus multiplier when cooperating with other players (1.0 = no bonus)
    /// </summary>
    public double CoopBonus { get; set; } = 1.0;
    /// <summary>
    /// Task type classification (Active, Passive, Limited)
    /// </summary>
    public TaskType Type { get; set; } = TaskType.Active;
}

public class RequiredItem
{
    public string ItemTag { get; set; }
    public string Name { get; set; }
    /// <summary>
    /// Why this item is needed (e.g. "Main weapon", "Armor set", "Tool")
    /// </summary>
    public string Reason { get; set; }
    /// <summary>
    /// Estimated price at time of calculation (0 if unknown)
    /// </summary>
    public long EstimatedPrice { get; set; }
}

public class DropInfo
{
    public string ItemTag { get; set; }
    public string Name { get; set; }
    public double RatePerHour { get; set; }
    public double PriceEach { get; set; }
    public double ContributionPerHour { get; set; }
}

public class DropEffect
{
    /// <summary>
    /// Name of the effect (e.g. "Luck VII", "Mining Speed boost", "Pet ability")
    /// </summary>
    public string Name { get; set; }
    /// <summary>
    /// Description of how it affects drops/speed
    /// </summary>
    public string Description { get; set; }
    /// <summary>
    /// Estimated multiplier on profit (1.0 = no change, 1.2 = 20% increase)
    /// </summary>
    public double EstimatedMultiplier { get; set; } = 1.0;
}
