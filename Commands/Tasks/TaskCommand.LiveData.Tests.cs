using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;
using FluentAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Deterministic mock-data tests for the full task pipeline.
/// Replaces the previous live-data integration test that required a running PlayerState service.
/// </summary>
public class TaskCommandMockDataTests
{
    // ── Shared test helpers ──

    private static Period MakePeriod(string location, long profit, Dictionary<string, int> items, int minutesDuration = 5)
    {
        var start = new DateTime(2025, 7, 24, 12, 0, 0);
        return new Period
        {
            PlayerUuid = "test-player",
            Server = "m1",
            Location = location,
            Profit = profit,
            StartTime = start,
            EndTime = start.AddMinutes(minutesDuration),
            ItemsCollected = items
        };
    }

    private static TaskParams MakeParams(
        Dictionary<string, long> cleanPrices = null,
        Dictionary<string, string> names = null,
        params Period[] periods)
    {
        return new TaskParams
        {
            TestTime = new DateTime(2025, 7, 24, 17, 0, 0),
            ExtractedInfo = new ExtractedInfo(),
            Formatter = new SimpleTaskFormatProvider(),
            Cache = new ConcurrentDictionary<Type, TaskParams.CalculationCache>(),
            MaxAvailableCoins = 1_000_000_000,
            LocationProfit = periods
                .GroupBy(p => p.Location ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.ToArray()),
            CleanPrices = cleanPrices ?? new Dictionary<string, long>(),
            BazaarPrices = [],
            Names = names ?? new Dictionary<string, string>()
        };
    }

    private static List<ProfitTask> GetRegisteredTasks()
    {
        var command = new TaskCommand();
        var field = typeof(TaskCommand).GetField("_tasks", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("TaskCommand should contain a _tasks field");

        var value = field!.GetValue(command);
        value.Should().NotBeNull("_tasks should be initialized by TaskCommand constructor");

        if (value is not IEnumerable enumerable)
        {
            throw new InvalidOperationException("Task registry is not enumerable.");
        }

        var tasks = new List<ProfitTask>();
        foreach (var entry in enumerable)
        {
            if (entry == null) continue;
            var entryType = entry.GetType();
            var taskValue = entryType.GetProperty("Value")?.GetValue(entry);
            if (taskValue is ProfitTask task)
                tasks.Add(task);
        }
        return tasks;
    }

    // ── Core pipeline tests ──

    [Test]
    public void AllRegisteredTasks_ShouldBeNonEmpty()
    {
        var tasks = GetRegisteredTasks();
        tasks.Should().NotBeEmpty("TaskCommand should register tasks to evaluate");
        tasks.Count.Should().BeGreaterThan(50, "we expect at least 50+ registered money-making methods");
    }

    [Test]
    public async Task AllTasks_ExecuteWithoutExceptions_WhenNoData()
    {
        var tasks = GetRegisteredTasks();
        var parameters = MakeParams();

        var results = await Task.WhenAll(tasks.Select(async task =>
        {
            try
            {
                return await task.Execute(parameters);
            }
            catch (Exception e)
            {
                return new TaskResult
                {
                    ProfitPerHour = 0,
                    Name = task.Name,
                    Message = $"EXCEPTION: {e.Message}",
                    Details = e.ToString()
                };
            }
        }));

        // Tasks that require DI services (GetService<T>) are expected to fail when Socket is null
        var diDependentNames = new HashSet<string> { "Kat", "Composter", "ShimmeringLightSlippers", "ExtremelyRealShuriken", "ShimmeringLightHood", "PolarvoidBook", "GrandmasKnittingNeedle", "SoulOfTheAlpha", "BluetoothRing", "Discrite", "CaducousFeeder" };
        var exceptions = results.Where(r => r.Message?.StartsWith("EXCEPTION:") == true && !diDependentNames.Contains(r.Name)).ToList();
        exceptions.Should().BeEmpty($"Non-DI tasks should not throw exceptions. Failed: {string.Join(", ", exceptions.Select(e => e.Name))}");
        results.Length.Should().Be(tasks.Count, "all registered tasks should produce a result");
    }

    [Test]
    public async Task AllTasks_ExecuteWithoutExceptions_WithMockPlayerData()
    {
        var tasks = GetRegisteredTasks();
        var prices = new Dictionary<string, long>
        {
            { "SHARD_CINDERBAT", 2000 }, { "SHARD_BURNINGSOUL", 1500 }, { "SHARD_LUMISQUID", 1800 },
            { "SHARD_DROWNED", 2500 }, { "SHARD_YOG", 3000 }, { "GHOST_COIN", 100 },
            { "SUMMONING_EYE", 800_000 }, { "ENCHANTED_RED_MUSHROOM", 5000 },
            { "WITHER_ESSENCE", 20 }, { "NECRON_HANDLE", 300_000_000 },
            { "GRIFFIN_FEATHER", 15_000 }, { "DAEDALUS_STICK", 50_000_000 },
            { "NULL_SPHERE", 100_000 }, { "ENCHANTED_CROP", 10_000 },
            { "ENCHANTED_FIG", 12_000 }, { "RAW_FISH", 10 }, { "ENDER_PEARL", 5 },
            { "ENCHANTED_BROWN_MUSHROOM", 4000 }, { "ENCHANTED_MYCELIUM", 3500 },
            { "SHARD_RAIN_SLIME", 1200 }, { "SHARD_HELLWISP", 1100 },
            { "ROUGH_JADE_GEM", 50 }, { "FINE_JADE_GEM", 2000 },
            { "KISMET_FEATHER", 3_000_000 },
        };
        var names = prices.Keys.ToDictionary(k => k, k => k.Replace("_", " "));

        // Create diverse mock periods covering multiple method types
        var periods = new[]
        {
            MakePeriod("Dive-Ember Pass", 500_000, new() { { "SHARD_CINDERBAT", 42 } }),
            MakePeriod("Dive-Ember Pass", 400_000, new() { { "SHARD_BURNINGSOUL", 30 } }),
            MakePeriod("Kelpwoven Tunnels", 600_000, new() { { "SHARD_DROWNED", 55 } }),
            MakePeriod("Magma Fields", 800_000, new() { { "SHARD_YOG", 35 } }),
            MakePeriod("The Mist", 1_200_000, new() { { "GHOST_COIN", 450 }, { "SHARD_GHOST", 10 } }),
            MakePeriod("The End", 3_000_000, new() { { "SUMMONING_EYE", 5 }, { "NULL_SPHERE", 15 } }, 10),
            MakePeriod("Dragon's Nest", 2_000_000, new() { { "ENDER_PEARL", 50 }, { "SUMMONING_EYE", 2 } }, 10),
            MakePeriod("Hub", 2_000_000, new() { { "GRIFFIN_FEATHER", 15 } }),
            MakePeriod("The Catacombs", 10_000_000, new() { { "KISMET_FEATHER", 3 }, { "WITHER_ESSENCE", 600 } }, 15),
            MakePeriod("Piscary", 200_000, new() { { "RAW_FISH", 300 }, { "ENCHANTED_RAW_FISH", 25 } }),
            MakePeriod("Mushroom Desert", 300_000, new() { { "RED_MUSHROOM", 100 }, { "ENCHANTED_RED_MUSHROOM", 20 } }),
            MakePeriod("Crystal Hollows", 1_500_000, new() { { "ROUGH_JADE_GEM", 500 }, { "FINE_JADE_GEM", 20 } }),
            MakePeriod("Stronghold", 5_000_000, new() { { "BLAZE_ROD", 500 } }, 20),
        };

        var parameters = MakeParams(prices, names, periods);

        var results = await Task.WhenAll(tasks.Select(async task =>
        {
            try
            {
                return await task.Execute(parameters);
            }
            catch (Exception e)
            {
                return new TaskResult
                {
                    ProfitPerHour = 0,
                    Name = task.Name,
                    Message = $"EXCEPTION: {e.Message}",
                    Details = e.ToString()
                };
            }
        }));

        // Tasks that require DI services (GetService<T>) are expected to fail when Socket is null
        var diDependentNames = new HashSet<string> { "Kat", "Composter", "ShimmeringLightSlippers", "ExtremelyRealShuriken", "ShimmeringLightHood", "PolarvoidBook", "GrandmasKnittingNeedle", "SoulOfTheAlpha", "BluetoothRing", "Discrite", "CaducousFeeder" };
        var exceptions = results.Where(r => r.Message?.StartsWith("EXCEPTION:") == true && !diDependentNames.Contains(r.Name)).ToList();
        exceptions.Should().BeEmpty($"Non-DI tasks should not throw. Failed: {string.Join(", ", exceptions.Select(e => $"{e.Name}: {e.Details?.Split('\n').FirstOrDefault()}"))}");

        var positives = results.Where(r => r.ProfitPerHour > 0).OrderByDescending(r => r.ProfitPerHour).ToList();
        positives.Count.Should().BeGreaterThan(5, "mock data should produce at least 5 profitable tasks");

        foreach (var result in positives)
        {
            result.ProfitPerHour.Should().BeLessThan(2_000_000_000, $"{result.Name} profit is unreasonably high");
            result.Message.Should().NotBeNullOrWhiteSpace($"{result.Name} should have a message");
        }

        TestContext.WriteLine($"Total results: {results.Length}, Positive: {positives.Count}");
        foreach (var r in positives.Take(15))
        {
            var label = string.IsNullOrWhiteSpace(r.Name) ? "<unnamed>" : r.Name;
            TestContext.WriteLine($"  {label} => {r.ProfitPerHour:N0}/h | {r.Message}");
        }
    }

    // ── MethodBreakdown population tests ──

    [Test]
    public async Task MethodTasks_PopulateBreakdown_WithCategory()
    {
        var tasks = GetRegisteredTasks().OfType<MethodTask>().ToList();
        tasks.Should().NotBeEmpty();

        var prices = new Dictionary<string, long>
        {
            { "SHARD_CINDERBAT", 2000 }, { "WITHER_ESSENCE", 20 },
            { "NECRON_HANDLE", 300_000_000 }, { "SUMMONING_EYE", 800_000 }
        };
        var names = prices.Keys.ToDictionary(k => k, k => k.Replace("_", " "));

        // Use formula path (no periods) to test metadata population
        var parameters = MakeParams(prices, names);

        foreach (var task in tasks.Where(t => t.FormulaDropsForTest.Count > 0))
        {
            var result = await task.Execute(parameters);
            if (result.Breakdown != null)
            {
                result.Breakdown.Category.Should().NotBeNullOrEmpty($"{task.Name} should have a Category in its breakdown");
                result.Breakdown.Source.Should().NotBeNullOrEmpty($"{task.Name} should have a Source");
            }
        }
    }

    [Test]
    public async Task FormulaPath_ProducesEstimatedProfit_WhenPricesAvailable()
    {
        var prices = new Dictionary<string, long>
        {
            { "SHARD_CINDERBAT", 2000 },
            { "SHARD_YOG", 3000 },
            { "WITHER_ESSENCE", 20 },
            { "NECRON_HANDLE", 300_000_000 }
        };
        var names = prices.Keys.ToDictionary(k => k, k => k.Replace("_", " "));
        var parameters = MakeParams(prices, names);

        // Cinderbat: 300 SHARD_CINDERBAT/h * 2000 = 600,000/h
        var cinderbat = new CinderbatTask();
        var result = await cinderbat.Execute(parameters);
        result.ProfitPerHour.Should().BeGreaterThan(0, "Formula should produce positive profit when prices exist");
        result.Details.Should().Contain("Estimated");
        result.Breakdown.Should().NotBeNull();
        result.Breakdown.Source.Should().Be("formula");

        // M7: 600 WITHER_ESSENCE/h * 20 + 0.05 NECRON_HANDLE/h * 300M = 15M + 12k = ~15M/h
        var m7 = new M7Task();
        var m7Result = await m7.Execute(parameters);
        m7Result.ProfitPerHour.Should().BeGreaterThan(10_000, "M7 formula should produce substantial profit with handle drops");
    }

    [Test]
    public async Task PlayerDataPath_OverridesFormula_WhenPeriodsExist()
    {
        var prices = new Dictionary<string, long> { { "SHARD_CINDERBAT", 2000 } };
        var names = new Dictionary<string, string> { { "SHARD_CINDERBAT", "Cinderbat Shard" } };

        // Create periods that give different profit than formula
        var period = MakePeriod("Dive-Ember Pass", 1_000_000, new() { { "SHARD_CINDERBAT", 50 } }, 5);
        var parameters = MakeParams(prices, names, period);

        var task = new CinderbatTask();
        var result = await task.Execute(parameters);
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Breakdown.Should().NotBeNull();
        result.Breakdown.Source.Should().Be("player_data", "player data should take priority over formula");
    }

    // ── Sanity bounds ──

    [Test]
    public async Task NoTask_ExceedsSanityBound()
    {
        var tasks = GetRegisteredTasks();
        var prices = new Dictionary<string, long>
        {
            { "SHARD_CINDERBAT", 2000 }, { "SHARD_BURNINGSOUL", 1500 },
            { "WITHER_ESSENCE", 20 }, { "NECRON_HANDLE", 300_000_000 },
            { "SUMMONING_EYE", 800_000 }, { "NULL_SPHERE", 100_000 },
            { "GRIFFIN_FEATHER", 15_000 }, { "DAEDALUS_STICK", 50_000_000 },
            { "GHOST_COIN", 100 }, { "ENCHANTED_CROP", 10_000 },
        };
        var names = prices.Keys.ToDictionary(k => k, k => k.Replace("_", " "));
        var parameters = MakeParams(prices, names);

        var results = await Task.WhenAll(tasks.Select(async t =>
        {
            try { return await t.Execute(parameters); }
            catch { return new TaskResult { ProfitPerHour = 0, Name = t.Name }; }
        }));

        foreach (var result in results.Where(r => r.ProfitPerHour > 0))
        {
            result.ProfitPerHour.Should().BeLessThan(500_000_000,
                $"{result.Name} exceeds 500M/h sanity bound — likely a calculation bug");
        }
    }

    // ── Slayer IndividualSlayerTask now produces MethodBreakdown ──

    [Test]
    public async Task IndividualSlayerTask_ProducesBreakdown()
    {
        var period = MakePeriod("Stronghold", 5_000_000, new()
        {
            { "BLAZE_ROD", 500 },
            { "ENCHANTED_BLAZE_ROD", 10 }
        }, 20);

        var task = new T4InfernoDemonlordTask();
        var result = await task.Execute(MakeParams(periods: period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Breakdown.Should().NotBeNull("IndividualSlayerTask should now produce a breakdown");
        result.Breakdown.Category.Should().Be("Slayer");
        result.Breakdown.Source.Should().Be("player_data");
        result.Breakdown.Drops.Should().NotBeEmpty("slayer should report collected item drops");
    }

    [Test]
    public async Task IndividualSlayerTask_NoData_ShowsBreakdownWithNoneSource()
    {
        var task = new T4InfernoDemonlordTask();
        var result = await task.Execute(MakeParams());
        result.Breakdown.Should().NotBeNull();
        result.Breakdown.Category.Should().Be("Slayer");
        result.Breakdown.Source.Should().Be("none");
    }
}
