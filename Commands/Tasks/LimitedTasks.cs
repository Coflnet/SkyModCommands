using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Base class for limited tasks that can only be done once per day or every few hours.
/// Tracks cooldown via player state and marks as inaccessible when on cooldown.
/// </summary>
public abstract class LimitedTask : MethodTask
{
    protected override TaskType TaskType => TaskType.Limited;

    /// <summary>
    /// How often the task can be repeated (e.g. 24h for daily, 8h for Rift)
    /// </summary>
    protected abstract TimeSpan Cooldown { get; }

    /// <summary>
    /// Maximum number of times this can be done per cooldown period (default 1)
    /// </summary>
    protected virtual int MaxUsesPerCooldown => 1;

    /// <summary>
    /// Detection items that indicate the task was recently completed
    /// (present in recent periods means it was done)
    /// </summary>
    protected virtual HashSet<string> CompletionDetectionItems => DetectionItems;

    protected override string CheckAccessibility(TaskParams parameters)
    {
        var nextAvailable = GetNextAvailableAt(parameters);
        if (nextAvailable.HasValue)
        {
            return $"Already done recently. Next available {FormatTimeUntil(nextAvailable.Value, parameters.TestTime)}.";
        }

        return base.CheckAccessibility(parameters);
    }

    protected override DateTime? GetNextAvailableAt(TaskParams parameters)
    {
        var recentPeriods = FindMatchingPeriods(parameters)
            .Where(p => p.EndTime > parameters.TestTime.Add(-Cooldown))
            .OrderByDescending(p => p.EndTime)
            .ToList();

        if (recentPeriods.Count < MaxUsesPerCooldown)
            return null;

        var relevantPeriod = recentPeriods.Take(MaxUsesPerCooldown).MinBy(p => p.EndTime);
        return relevantPeriod?.EndTime.Add(Cooldown);
    }

    private static string FormatTimeUntil(DateTime target, DateTime now)
    {
        var diff = target - now;
        if (diff.TotalMinutes < 1) return "now";
        if (diff.TotalHours < 1) return $"in {diff.Minutes}m";
        if (diff.TotalDays < 1) return $"in {diff.Hours}h {diff.Minutes}m";
        return $"in {diff.Days}d {diff.Hours}h";
    }
}

// ── Daily Crimson Quests ──
public class DailyCrimsonQuestsTask : LimitedTask
{
    protected override string MethodName => "Daily Crimson Quests";
    protected override TimeSpan Cooldown => TimeSpan.FromHours(24);
    protected override HashSet<string> Locations => ["Crimson Isle", "Stronghold", "Dragontail", "Burning Desert", "Blazing Volcano", "Mage Outpost", "The Dukedom"];
    protected override HashSet<string> DetectionItems => ["CRIMSON_QUEST_REWARD"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_QUEST_REWARD", 5)];
    protected override string Category => "Daily";
    protected override string HowTo => "Go to the Crimson Isle and complete daily quests from NPCs. " +
        "Quests include combat missions, item collection, and boss kills. Rewards include Crimson Essence and exclusive items.";
    protected override List<RequiredItem> RequiredItems => [
        new() { ItemTag = "ASPECT_OF_THE_DRAGONS", Reason = "Combat weapon for quest mobs" }
    ];
    protected override List<DropEffect> Effects => [
        new() { Name = "Combat Level", Description = "Higher combat makes quests faster", EstimatedMultiplier = 1.2 }
    ];
}

// ── Experimentation Table ──
public class ExperimentationTableTask : LimitedTask
{
    protected override string MethodName => "Experimentation Table";
    protected override TimeSpan Cooldown => TimeSpan.FromHours(24);
    protected override HashSet<string> Locations => ["Hub", "Village"];
    protected override HashSet<string> DetectionItems => ["EXPERIMENTATION_REWARD", "GRAND_EXPERIENCE_BOTTLE", "TITANIC_EXPERIENCE_BOTTLE"];
    protected override List<MethodDrop> FormulaDrops => [new("GRAND_EXPERIENCE_BOTTLE", 3), new("TITANIC_EXPERIENCE_BOTTLE", 1)];
    protected override string Category => "Daily";
    protected override string HowTo => "Visit the Experimentation Table in the Hub (near the Alchemist). " +
        "Complete the memory/click game for enchanting XP and rare rewards. Higher Enchanting level unlocks better experiments.";
    protected override List<RequiredItem> RequiredItems => [];
    protected override List<DropEffect> Effects => [
        new() { Name = "Enchanting Level", Description = "Higher enchanting unlocks Superpairs and Ultrasequencer", EstimatedMultiplier = 1.5 }
    ];
}

// ── Rift Access (3x per day) ──
public class RiftAccessTask : LimitedTask
{
    protected override string MethodName => "Rift Access";
    protected override TimeSpan Cooldown => TimeSpan.FromHours(8);
    protected override int MaxUsesPerCooldown => 1; // effectively 3 per day with 8h cooldown
    protected override HashSet<string> Locations => ["The Rift", "Wyld Woods", "Dreadfarm", "West Village", "Shifted Tavern"];
    protected override HashSet<string> DetectionItems => ["RIFT_PRISM", "MOTES"];
    protected override List<MethodDrop> FormulaDrops => [new("MOTES", 5000)];
    protected override string Category => "Daily";
    protected override string HowTo => "Enter the Rift using a Rift Prism (can be done 3 times per day). " +
        "Farm motes from mobs, complete timecharms, and collect Rift-exclusive items. Motes are the primary currency.";
    protected override List<RequiredItem> RequiredItems => [
        new() { ItemTag = "RIFT_PRISM", Reason = "Required to enter the Rift dimension" }
    ];
    protected override List<DropEffect> Effects => [
        new() { Name = "Soulflow", Description = "Powers special abilities in the Rift", EstimatedMultiplier = 1.1 },
        new() { Name = "Motes collection", Description = "Autopickup motes with higher collection", EstimatedMultiplier = 1.3 }
    ];
}

// ── Viper Shard NPC Flip ──
public class ViperShardNpcFlipTask : LimitedTask
{
    protected override string MethodName => "Viper Shard NPC Flip";
    protected override TimeSpan Cooldown => TimeSpan.FromHours(24);
    protected override HashSet<string> Locations => ["North Reaches"];
    protected override HashSet<string> DetectionItems => ["SHARD_VIPER"];
    protected override List<MethodDrop> FormulaDrops => []; // overriding Execute for custom calc
    protected override string Category => "Daily";
    protected override double ActionsPerHour => 20; // ~3 minutes per run = 20 runs/hour
    protected override string HowTo => "Buy 10 SHARD_VIPER from the NPC for 100k coins each (1M total), " +
        "then sell them on the bazaar at North Reaches for 150k-200k each. " +
        "Takes about 3 minutes. Up to 1M profit per run (~20M/h effective rate).";

    public override Task<TaskResult> Execute(TaskParams parameters)
    {
        var prices = parameters.GetPrices();
        var viperPrice = prices.GetValueOrDefault("SHARD_VIPER", 0);
        var npcBuyPrice = 100_000f;
        var quantity = 10;

        if (viperPrice <= npcBuyPrice)
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Type = TaskType.Limited,
                IsAccessible = false,
                InaccessibleReason = "Viper Shard bazaar price is below NPC buy price (100k).",
                Message = "Viper Shard NPC Flip - not profitable right now.",
                Name = MethodName
            });

        var profitPerRun = (viperPrice - npcBuyPrice) * quantity;
        var minutesPerRun = 3.0;
        var profitPerHour = profitPerRun / minutesPerRun * 60;

        var accessibilityIssue = CheckAccessibility(parameters);
        var fmt = parameters.Formatter;

        var result = new TaskResult
        {
            ProfitPerHour = (int)profitPerHour,
            Type = TaskType.Limited,
            Message = $"Buy 10 Viper Shards from NPC, sell on bazaar for {McColorCodes.AQUA}{fmt.FormatPrice(profitPerRun)} {McColorCodes.GRAY}profit",
            Details = $"NPC buy price: {McColorCodes.YELLOW}{fmt.FormatPrice(npcBuyPrice)} {McColorCodes.GRAY}x10 = {fmt.FormatPrice(npcBuyPrice * quantity)}\n"
                + $"Bazaar sell price: {McColorCodes.AQUA}{fmt.FormatPrice(viperPrice)} {McColorCodes.GRAY}x10 = {fmt.FormatPrice(viperPrice * quantity)}\n"
                + $"Profit: {McColorCodes.GREEN}{fmt.FormatPrice(profitPerRun)}\n"
                + $"Time: ~3 minutes\n"
                + $"Effective rate: {McColorCodes.AQUA}{fmt.FormatPrice(profitPerHour)}/h\n"
                + $"{McColorCodes.GOLD}Requirements: 1M coins to buy shards",
            Name = MethodName,
            OnClick = "/warp reaches",
            Breakdown = new MethodBreakdown
            {
                HowTo = HowTo,
                RequiredItems = [new RequiredItem { ItemTag = "SHARD_VIPER", Name = "Viper Shard", Reason = "Buy from NPC at North Reaches (100k each)" }],
                Drops = [new DropInfo
                {
                    ItemTag = "SHARD_VIPER",
                    Name = "Viper Shard (sell)",
                    RatePerHour = quantity / (minutesPerRun / 60),
                    PriceEach = viperPrice - npcBuyPrice,
                    ContributionPerHour = profitPerHour
                }],
                ActionsPerHour = 60 / minutesPerRun,
                ActionUnit = "runs",
                Category = "Daily",
                Source = "formula",
                Type = TaskType.Limited
            }
        };

        if (accessibilityIssue != null)
        {
            result.IsAccessible = false;
            result.InaccessibleReason = accessibilityIssue;
            result.NextAvailableAt = GetNextAvailableAt(parameters);
        }
        return Task.FromResult(result);
    }
}
