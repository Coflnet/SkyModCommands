using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;
using FluentAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class MethodDetectionTests
{
    private static Period MakePeriod(string location, long profit, Dictionary<string, int> items, int minutesDuration = 5)
    {
        var start = new DateTime(2025, 7, 24, 12, 0, 0);
        return new Period
        {
            PlayerUuid = "test",
            Server = "m1",
            Location = location,
            Profit = profit,
            StartTime = start,
            EndTime = start.AddMinutes(minutesDuration),
            ItemsCollected = items
        };
    }

    private static TaskParams MakeParams(params Period[] periods)
    {
        return new TaskParams
        {
            TestTime = new DateTime(2025, 7, 24, 17, 0, 0),
            ExtractedInfo = new ExtractedInfo(),
            Socket = new MinecraftSocket(),
            Cache = new ConcurrentDictionary<Type, TaskParams.CalculationCache>(),
            MaxAvailableCoins = 1_000_000_000,
            LocationProfit = periods.GroupBy(l => l.Location).ToDictionary(l => l.Key, l => l.ToArray()),
            CleanPrices = new Dictionary<string, long>(),
            BazaarPrices = [],
            Names = new Dictionary<string, string>()
        };
    }

    // ── Mob detection via SHARD_ items ──

    [Test]
    public async Task CinderbatDetected_ByShard()
    {
        var period = MakePeriod("Dive-Ember Pass", 500_000, new()
        {
            { "SHARD_CINDERBAT", 42 },
            { "AGATHA_COUPON", 10 }
        });
        var task = new CinderbatTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Cinderbat");
        result.Details.Should().Contain("SHARD_CINDERBAT");
    }

    [Test]
    public async Task BurningsoulDetected_NotCinderbat_AtSameLocation()
    {
        var period = MakePeriod("Dive-Ember Pass", 400_000, new()
        {
            { "SHARD_BURNINGSOUL", 30 },
            { "AGATHA_COUPON", 8 }
        });

        // Burningsoul should detect it
        var burningsoul = new BurningsoulTask();
        var bResult = await burningsoul.Execute(MakeParams(period));
        bResult.ProfitPerHour.Should().BeGreaterThan(0);
        bResult.Name.Should().Be("Burningsoul");

        // Cinderbat should NOT detect it (wrong shard)
        var cinderbat = new CinderbatTask();
        var cResult = await cinderbat.Execute(MakeParams(period));
        cResult.ProfitPerHour.Should().Be(0, "Cinderbat should not match periods with SHARD_BURNINGSOUL");
    }

    [Test]
    public async Task DrownedDetected_AtKelpwovenTunnels()
    {
        var period = MakePeriod("Kelpwoven Tunnels", 600_000, new()
        {
            { "SHARD_DROWNED", 55 },
            { "DEEP_ROOT", 3 }
        });
        var task = new DrownedTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Drowned");
    }

    // ── Fishing vs Fishing-Hunting distinction ──

    [Test]
    public async Task RegularFishing_ExcludesShardsFromResults()
    {
        // A fishing session that has SHARD_ items = hunting, not regular fishing
        var huntingPeriod = MakePeriod("Piscary", 300_000, new()
        {
            { "RAW_FISH", 200 },
            { "SHARD_SQUID", 5 }
        });

        var regularTask = new PiscaryFishingTask();
        var result = await regularTask.Execute(MakeParams(huntingPeriod));
        // Should NOT match because ExcludeShardItems is true and period has SHARD_ items
        result.ProfitPerHour.Should().Be(0, "Regular fishing should exclude periods with SHARD_ items");
    }

    [Test]
    public async Task RegularFishing_MatchesPeriodWithoutShards()
    {
        var normalPeriod = MakePeriod("Piscary", 200_000, new()
        {
            { "RAW_FISH", 300 },
            { "ENCHANTED_RAW_FISH", 25 }
        });

        var regularTask = new PiscaryFishingTask();
        var result = await regularTask.Execute(MakeParams(normalPeriod));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Piscary Fishing");
    }

    [Test]
    public async Task HuntingFishing_RequiresShardItems()
    {
        var huntingPeriod = MakePeriod("Piscary", 500_000, new()
        {
            { "RAW_FISH", 150 },
            { "SHARD_SQUID", 8 }
        });

        var huntingTask = new PiscaryFishingHuntingTask();
        var result = await huntingTask.Execute(MakeParams(huntingPeriod));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Contain("Hunting");
    }

    [Test]
    public async Task HuntingFishing_RejectsPeriodsWithoutShards()
    {
        var normalPeriod = MakePeriod("Piscary", 200_000, new()
        {
            { "RAW_FISH", 300 },
            { "ENCHANTED_RAW_FISH", 25 }
        });

        var huntingTask = new PiscaryFishingHuntingTask();
        var result = await huntingTask.Execute(MakeParams(normalPeriod));
        // No matching periods (requires shards but none present) and no prices → 0 profit
        result.ProfitPerHour.Should().Be(0);
    }

    // ── Hunting tasks (non-fishing) ──

    [Test]
    public async Task YogHunting_DetectedByShard()
    {
        var period = MakePeriod("Magma Fields", 800_000, new()
        {
            { "SHARD_YOG", 35 },
            { "ENCHANTED_MAGMA_CREAM", 12 }
        });
        var task = new YogHuntingTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Yog (Hunting)");
    }

    [Test]
    public async Task GhostHunting_DetectedByGhostCoin()
    {
        var period = MakePeriod("The Mist", 1_200_000, new()
        {
            { "GHOST_COIN", 450 },
            { "SHARD_GHOST", 10 }
        });
        var task = new GhostHuntingTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Ghost (Hunting)");
    }

    // ── Diana tasks ──

    [Test]
    public async Task Diana_DetectedByGriffinFeather()
    {
        var period = MakePeriod("Hub", 2_000_000, new()
        {
            { "GRIFFIN_FEATHER", 15 },
            { "ENCHANTED_GOLD", 8 }
        });
        var task = new DianaTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Diana");
    }

    [Test]
    public async Task DianaHunting_DetectedByKingMinos()
    {
        var period = MakePeriod("Wilderness", 5_000_000, new()
        {
            { "SHARD_KING_MINOS", 3 },
            { "GRIFFIN_FEATHER", 20 },
            { "DAEDALUS_STICK", 1 }
        });
        var task = new DianaHuntingTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Diana (Hunting)");
    }

    // ── Mining tasks ──

    [Test]
    public async Task JadeMining_DetectedByJadeGem()
    {
        var period = MakePeriod("Crystal Hollows", 1_500_000, new()
        {
            { "ROUGH_JADE_GEM", 500 },
            { "FINE_JADE_GEM", 20 }
        });
        var task = new JadeMiningTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Jade Mining");
    }

    // ── Multiple periods aggregation ──

    [Test]
    public async Task MultiplePeriodsAggregated_ForSameMob()
    {
        var p1 = MakePeriod("Dive-Ember Pass", 300_000, new() { { "SHARD_CINDERBAT", 30 } }, 5);
        var p2 = MakePeriod("Stride-Ember Fissure", 400_000, new() { { "SHARD_CINDERBAT", 40 } }, 5);

        var task = new CinderbatTask();
        var result = await task.Execute(MakeParams(p1, p2));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        // Both periods contribute to total
        result.Details.Should().Contain("SHARD_CINDERBAT");
    }

    // ── Overlapping locations resolved by items ──

    [Test]
    public async Task OverlappingLocations_ResolvedByDetectionItems()
    {
        // Dive-Ember Pass is shared by Cinderbat, Burningsoul, and Stridersurfer
        var cinderbatPeriod = MakePeriod("Dive-Ember Pass", 500_000, new() { { "SHARD_CINDERBAT", 40 } });
        var burningsoulPeriod = MakePeriod("Dive-Ember Pass", 400_000, new() { { "SHARD_BURNINGSOUL", 30 } });
        var stridersurferPeriod = MakePeriod("Stride-Ember Fissure", 600_000, new() { { "SHARD_STRIDERSURFER", 50 } });

        var allPeriods = new[] { cinderbatPeriod, burningsoulPeriod, stridersurferPeriod };
        var p = MakeParams(allPeriods);

        // Each task should only pick up its own periods
        var cinderbatResult = await new CinderbatTask().Execute(p);
        var burningsoulResult = await new BurningsoulTask().Execute(p);
        var stridersurferResult = await new StridersurferTask().Execute(p);

        cinderbatResult.ProfitPerHour.Should().BeGreaterThan(0);
        burningsoulResult.ProfitPerHour.Should().BeGreaterThan(0);
        stridersurferResult.ProfitPerHour.Should().BeGreaterThan(0);

        // Each should only see its own items
        cinderbatResult.Details.Should().Contain("SHARD_CINDERBAT").And.NotContain("SHARD_BURNINGSOUL");
        burningsoulResult.Details.Should().Contain("SHARD_BURNINGSOUL").And.NotContain("SHARD_CINDERBAT");
        stridersurferResult.Details.Should().Contain("SHARD_STRIDERSURFER");
    }

    // ── Dungeon tasks ──

    [Test]
    public async Task M7KismetDetection()
    {
        var period = MakePeriod("The Catacombs", 10_000_000, new()
        {
            { "KISMET_FEATHER", 3 },
            { "ENCHANTED_DIAMOND", 20 }
        }, 15);
        var task = new M7KismetTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("M7 (Kismet)");
    }

    // ── Galatea diving vs Galatea mob disambiguation ──

    [Test]
    public async Task GalateaDiving_MatchesGalateaLocations()
    {
        // GalateaDivingTask is an IslandTask that matches all diving locations
        // Mob tasks use SHARD_ detection to disambiguate
        var period = MakePeriod("Drowned Reliquary", 500_000, new()
        {
            { "SHARD_DROWNED", 40 },
            { "DEEP_ROOT", 5 }
        });

        // DrownedTask detects by SHARD_DROWNED
        var drownedResult = await new DrownedTask().Execute(MakeParams(period));
        drownedResult.ProfitPerHour.Should().BeGreaterThan(0);
        drownedResult.Name.Should().Be("Drowned");
    }

    // ── No data falls back to formula ──

    [Test]
    public async Task FormulaFallback_WhenNoPlayerData()
    {
        // Empty location profit = no matching periods
        var p = new TaskParams
        {
            TestTime = new DateTime(2025, 7, 24, 17, 0, 0),
            ExtractedInfo = new ExtractedInfo(),
            Socket = new MinecraftSocket(),
            Cache = new ConcurrentDictionary<Type, TaskParams.CalculationCache>(),
            MaxAvailableCoins = 1_000_000_000,
            LocationProfit = new Dictionary<string, Period[]>(),
            CleanPrices = new Dictionary<string, long>
            {
                { "SHARD_CINDERBAT", 2000 }
            },
            BazaarPrices = [],
            Names = new Dictionary<string, string>
            {
                { "SHARD_CINDERBAT", "Cinderbat Shard" }
            }
        };

        var task = new CinderbatTask();
        var result = await task.Execute(p);
        // With price data, formula should give an estimate
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Details.Should().Contain("Estimated");
    }

    // ── Slayer tasks ──

    [Test]
    public async Task VoidgloomsDetection()
    {
        var period = MakePeriod("The End", 3_000_000, new()
        {
            { "SUMMONING_EYE", 5 },
            { "ENCHANTED_OBSIDIAN", 15 }
        }, 10);
        var task = new T4VoidgloomsTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("T4 Voidglooms");
    }

    // ── Misc tasks ──

    [Test]
    public async Task ZealotsFd_DetectedByEnderPearl()
    {
        var period = MakePeriod("Dragon's Nest", 2_000_000, new()
        {
            { "ENDER_PEARL", 50 },
            { "SUMMONING_EYE", 2 }
        }, 10);
        var task = new ZealotsFdTask();
        var result = await task.Execute(MakeParams(period));
        result.ProfitPerHour.Should().BeGreaterThan(0);
        result.Name.Should().Be("Zealots (FD)");
    }
}
