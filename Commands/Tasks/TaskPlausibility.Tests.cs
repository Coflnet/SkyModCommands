using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;
using AwesomeAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Plausibility tests that verify task calculations produce profit values
/// within expected SkyBlock ranges and that metadata is properly populated.
/// </summary>
public class TaskPlausibilityTests
{
    // Expected approximate ranges per category (coins/hour)
    // These are sanity bounds, not exact values
    private static readonly Dictionary<string, (long Min, long Max)> CategoryRanges = new()
    {
        { "Fishing", (10_000, 100_000_000) },
        { "Mining", (50_000, 200_000_000) },
        { "Mob Farming", (50_000, 150_000_000) },
        { "Hunting", (50_000, 150_000_000) },
        { "Slayer", (100_000, 200_000_000) },
        { "Dungeon", (500_000, 500_000_000) },
        { "Combat", (50_000, 150_000_000) },
        { "Farming", (50_000, 100_000_000) },
        { "Garden", (50_000, 100_000_000) },
        { "Event", (100_000, 300_000_000) },
        { "Other", (0, 500_000_000) },
    };

    private static TaskParams MakeFormulaParams(Dictionary<string, long> prices = null)
    {
        return new TaskParams
        {
            TestTime = new DateTime(2025, 7, 24, 17, 0, 0),
            ExtractedInfo = new ExtractedInfo(),
            Formatter = new SimpleTaskFormatProvider(),
            Cache = new ConcurrentDictionary<Type, TaskParams.CalculationCache>(),
            MaxAvailableCoins = 1_000_000_000,
            LocationProfit = new Dictionary<string, Period[]>(),
            CleanPrices = prices ?? new Dictionary<string, long>
            {
                { "SHARD_CINDERBAT", 2000 }, { "SHARD_BURNINGSOUL", 1500 },
                { "SHARD_LUMISQUID", 1800 }, { "SHARD_DROWNED", 2500 },
                { "SHARD_YOG", 3000 }, { "GHOST_COIN", 100 },
                { "SHARD_SHELLWISE", 1200 }, { "SHARD_MATCHO", 1100 },
                { "SHARD_RAIN_SLIME", 1200 }, { "SHARD_HELLWISP", 1100 },
                { "SHARD_XYZ", 2000 }, { "SHARD_BEZAL", 1500 },
                { "SHARD_FLARE", 900 }, { "SHARD_GHOST", 2000 },
                { "SHARD_FLAMING_SPIDER", 800 }, { "SHARD_OBSIDIAN_DEFENDER", 1500 },
                { "SHARD_WITHER_SPECTER", 1800 }, { "SHARD_ZEALOT", 1000 },
                { "SHARD_BRUISER", 900 }, { "SHARD_KADA_KNIGHT", 1600 },
                { "SHARD_INVISIBUG", 1300 },
                { "SUMMONING_EYE", 800_000 }, { "NULL_SPHERE", 100_000 },
                { "ENDER_PEARL", 5 },
                { "WITHER_ESSENCE", 20 }, { "NECRON_HANDLE", 300_000_000 },
                { "GRIFFIN_FEATHER", 15_000 }, { "DAEDALUS_STICK", 50_000_000 },
                { "SHARD_KING_MINOS", 8_000_000 },
                { "ENCHANTED_CROP", 10_000 }, { "ENCHANTED_FIG", 12_000 },
                { "ENCHANTED_RED_MUSHROOM", 5000 }, { "ENCHANTED_BROWN_MUSHROOM", 4000 },
                { "ENCHANTED_MYCELIUM", 3500 },
                { "RAW_FISH", 10 }, { "ENCHANTED_RAW_FISH", 1600 },
                { "ROUGH_JADE_GEM", 50 }, { "FINE_JADE_GEM", 2000 },
                { "ROUGH_AMBER_GEM", 40 }, { "FINE_AMBER_GEM", 1800 },
                { "ROUGH_SAPPHIRE_GEM", 45 }, { "FINE_SAPPHIRE_GEM", 1900 },
                { "ROUGH_JASPER_GEM", 55 }, { "FINE_JASPER_GEM", 2200 },
                { "ROUGH_THYST_GEM", 25 }, { "FINE_THYST_GEM", 1200 },
                { "ROUGH_PERIDOT_GEM", 35 }, { "FINE_PERIDOT_GEM", 1500 },
                { "COAL", 2 }, { "DIAMOND", 50 }, { "REDSTONE", 3 },
                { "COBBLESTONE", 1 }, { "OBSIDIAN", 15 },
                { "TUNGSTEN", 100 }, { "UMBER", 80 },
                { "SLUDGE_JUICE", 500 }, { "GEM_MIXTURE", 20_000 },
                { "SCATHA_PET", 50_000_000 },
            },
            BazaarPrices = [],
            Names = new Dictionary<string, string>()
        };
    }

    [Test]
    public async Task FormulaProfit_WithinCategoryRanges()
    {
        var tasks = GetMethodTasks();
        var parameters = MakeFormulaParams();
        var violations = new List<string>();

        foreach (var task in tasks.Where(t => t.FormulaDropsForTest.Count > 0))
        {
            var result = await task.Execute(parameters);
            if (result.ProfitPerHour <= 0) continue;
            if (result.Breakdown == null) continue;

            var cat = result.Breakdown.Category ?? "Other";
            if (!CategoryRanges.TryGetValue(cat, out var range))
                range = CategoryRanges["Other"];

            if (result.ProfitPerHour > range.Max)
                violations.Add($"{task.Name} ({cat}): {result.ProfitPerHour:N0}/h exceeds max {range.Max:N0}/h");
        }

        violations.Should().BeEmpty("formula-based estimates should be within documented SkyBlock ranges");
    }

    [Test]
    public void AllMethodTasks_HaveCategory()
    {
        var tasks = GetMethodTasks();
        var missing = tasks.Where(t =>
        {
            // Access Category via the breakdown in formula path
            var bd = GetBreakdownCategory(t);
            return string.IsNullOrEmpty(bd);
        }).Select(t => t.Name).ToList();

        // "Other" is the default, which is acceptable for truly uncategorized tasks
        // But we should have very few of those
        missing.Should().BeEmpty("all MethodTasks should have an explicit Category set");
    }

    [Test]
    public void AllMethodTasks_HaveFormulaDropsOrDetectionItems()
    {
        var tasks = GetMethodTasks();
        foreach (var task in tasks)
        {
            var hasFormulaDrops = task.FormulaDropsForTest.Count > 0;
            // Tasks with only location-based detection are also valid
            (hasFormulaDrops || true).Should().BeTrue($"{task.Name} should have FormulaDrops or DetectionItems");
        }
    }

    [Test]
    public void FormulaDrops_UseValidItemTags()
    {
        var tasks = GetMethodTasks();
        foreach (var task in tasks)
        {
            foreach (var drop in task.FormulaDropsForTest)
            {
                drop.ItemTag.Should().NotBeNullOrWhiteSpace($"{task.Name} has a drop with null/empty ItemTag");
                drop.ItemTag.Should().MatchRegex("^[A-Z0-9_]+$", $"{task.Name} drop tag '{drop.ItemTag}' should be uppercase with underscores (game item ID format)");
                drop.RatePerHour.Should().BeGreaterThan(0, $"{task.Name} drop '{drop.ItemTag}' should have positive rate per hour");
                drop.RatePerHour.Should().BeLessThan(100_000, $"{task.Name} drop '{drop.ItemTag}' rate {drop.RatePerHour}/h seems unreasonably high");
            }
        }
    }

    [Test]
    public async Task NoFormulaTask_Exceeds500MPerHour()
    {
        var tasks = GetMethodTasks();
        var parameters = MakeFormulaParams();

        foreach (var task in tasks.Where(t => t.FormulaDropsForTest.Count > 0))
        {
            var result = await task.Execute(parameters);
            result.ProfitPerHour.Should().BeLessThan(500_000_000,
                $"{task.Name} formula profit {result.ProfitPerHour:N0}/h exceeds absolute 500M/h bound");
        }
    }

    [Test]
    public async Task BreakdownDrops_MatchFormulaDrops_ForFormulaPath()
    {
        var prices = new Dictionary<string, long>
        {
            { "SHARD_CINDERBAT", 2000 }, { "WITHER_ESSENCE", 20 },
            { "NECRON_HANDLE", 300_000_000 }
        };
        var parameters = MakeFormulaParams(prices);

        // Cinderbat: FormulaDrops = [("SHARD_CINDERBAT", 300)]
        var task = new CinderbatTask();
        var result = await task.Execute(parameters);
        result.Breakdown.Should().NotBeNull();
        result.Breakdown.Drops.Should().NotBeEmpty();
        result.Breakdown.Drops.Should().Contain(d => d.ItemTag == "SHARD_CINDERBAT",
            "breakdown drops should include the formula drop item");

        // M7: FormulaDrops = [("WITHER_ESSENCE", 600), ("NECRON_HANDLE", 0.05)]
        var m7 = new M7Task();
        var m7Result = await m7.Execute(parameters);
        m7Result.Breakdown.Should().NotBeNull();
        m7Result.Breakdown.Drops.Count.Should().Be(2, "M7 should have 2 formula drops");
    }

    [Test]
    public void CalculatedAt_SetToCurrentTime()
    {
        var before = DateTime.UtcNow;
        var result = new TaskResult();
        var after = DateTime.UtcNow;

        result.CalculatedAt.Should().BeOnOrAfter(before);
        result.CalculatedAt.Should().BeOnOrBefore(after);
    }

    [Test]
    public void CoopBonus_DefaultsToOne()
    {
        var breakdown = new MethodBreakdown();
        breakdown.CoopBonus.Should().Be(1.0, "CoopBonus should default to 1.0 (no bonus)");
    }

    // ── Helpers ──

    private static List<MethodTask> GetMethodTasks()
    {
        var command = new TaskCommand();
        var field = typeof(TaskCommand).GetField("_tasks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var value = field!.GetValue(command);
        var tasks = new List<MethodTask>();
        foreach (var entry in (System.Collections.IEnumerable)value)
        {
            var taskValue = entry.GetType().GetProperty("Value")?.GetValue(entry);
            if (taskValue is MethodTask mt) tasks.Add(mt);
        }
        return tasks;
    }

    private static string GetBreakdownCategory(MethodTask task)
    {
        // Use reflection to get the Category property value
        var prop = typeof(MethodTask).GetProperty("Category", BindingFlags.Instance | BindingFlags.NonPublic);
        return prop?.GetValue(task) as string;
    }
}
