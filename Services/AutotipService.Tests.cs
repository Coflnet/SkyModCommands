using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.ModCommands.Models;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

#pragma warning disable CS0101
public class AutotipServiceTests
{
    [Test]
    public void FindGamemodeNeedingTip_ReturnsOldestGamemode()
    {
        // Create tips for all gamemodes except blitz, with arcade being most recent
        var now = DateTimeOffset.UtcNow;
        var recentTips = AutotipService.SupportedGamemodes
            .Where(gm => gm != "blitz")
            .Select((gm, index) => new AutotipEntry
            {
                Gamemode = gm,
                TippedAt = now.AddMinutes(-45 - index * 5) // Stagger the times
            })
            .ToList();
        
        // Add blitz as the oldest tipped gamemode
        recentTips.Add(new AutotipEntry { Gamemode = "blitz", TippedAt = now.AddMinutes(-120) });

        var result = AutotipServiceTestHelper.FindGamemodeNeedingTip("user1", recentTips);

        Assert.That(result, Is.EqualTo("blitz"));
    }

    [Test]
    public void FindGamemodeNeedingTip_ReturnsUntippedGamemode()
    {
        var recentTips = new List<AutotipEntry>
        {
            new() { Gamemode = "arcade", TippedAt = DateTimeOffset.UtcNow.AddMinutes(-45) }
        };

        var result = AutotipServiceTestHelper.FindGamemodeNeedingTip("user1", recentTips);

        // Should return a gamemode that hasn't been tipped
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.EqualTo("arcade"));
    }

    [Test]
    public void FindGamemodeNeedingTip_ReturnsNull_WhenAllTippedRecently()
    {
        var now = DateTimeOffset.UtcNow;
        var recentTips = AutotipService.SupportedGamemodes.Select(gm => new AutotipEntry
        {
            Gamemode = gm,
            TippedAt = now.AddMinutes(-15) // Less than 30 minutes ago
        }).ToList();

        var result = AutotipServiceTestHelper.FindGamemodeNeedingTip("user1", recentTips);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SelectBooster_PrioritizesMultipleNetworkBoosters()
    {
        var boosters = new List<ActiveBooster>
        {
            new() { purchaserName = "Player1", gamemode = "arcade" },
            new() { purchaserName = "Player2", gamemode = "skywars" },
            new() { purchaserName = "Player2", gamemode = "blitz" },
            new() { purchaserName = "Player2", gamemode = "megawalls" },
            new() { purchaserName = "Player3", gamemode = "uhc" },
            new() { purchaserName = "Player3", gamemode = "cnc" }
        };

        var boosterCounts = new Dictionary<string, int>
        {
            { "Player1", 1 },
            { "Player2", 3 },
            { "Player3", 2 }
        };

        var tippedToday = new HashSet<string>();
        var tippedLastHour = new HashSet<string>();
        var blacklist = new HashSet<string>();

        var arcadeBoosters = boosters.Where(b => b.gamemode == "arcade").ToList();
        var result = AutotipServiceTestHelper.SelectBoosterFromCandidates(
            arcadeBoosters, 
            boosterCounts, 
            tippedToday, 
            tippedLastHour, 
            blacklist
        );

        // Should select Player1 since they're the only one with arcade booster
        Assert.That(result, Is.EqualTo("Player1"));
    }

    [Test]
    public void SelectBooster_ExcludesTippedInLastHour()
    {
        var boosters = new List<ActiveBooster>
        {
            new() { purchaserName = "Player1", gamemode = "arcade" },
            new() { purchaserName = "Player2", gamemode = "arcade" }
        };

        var boosterCounts = new Dictionary<string, int>
        {
            { "Player1", 2 },
            { "Player2", 1 }
        };

        var tippedToday = new HashSet<string>();
        var tippedLastHour = new HashSet<string> { "Player1" };
        var blacklist = new HashSet<string>();

        var result = AutotipServiceTestHelper.SelectBoosterFromCandidates(
            boosters, 
            boosterCounts, 
            tippedToday, 
            tippedLastHour, 
            blacklist
        );

        Assert.That(result, Is.EqualTo("Player2"));
    }

    [Test]
    public void SelectBooster_ExcludesTippedToday()
    {
        var boosters = new List<ActiveBooster>
        {
            new() { purchaserName = "Player1", gamemode = "arcade" },
            new() { purchaserName = "Player2", gamemode = "arcade" }
        };

        var boosterCounts = new Dictionary<string, int>
        {
            { "Player1", 2 },
            { "Player2", 1 }
        };

        var tippedToday = new HashSet<string> { "Player1" };
        var tippedLastHour = new HashSet<string>();
        var blacklist = new HashSet<string>();

        var result = AutotipServiceTestHelper.SelectBoosterFromCandidates(
            boosters, 
            boosterCounts, 
            tippedToday, 
            tippedLastHour, 
            blacklist
        );

        Assert.That(result, Is.EqualTo("Player2"));
    }

    [Test]
    public void SelectBooster_ExcludesBlacklisted()
    {
        var boosters = new List<ActiveBooster>
        {
            new() { purchaserName = "Player1", gamemode = "arcade" },
            new() { purchaserName = "Player2", gamemode = "arcade" }
        };

        var boosterCounts = new Dictionary<string, int>
        {
            { "Player1", 2 },
            { "Player2", 1 }
        };

        var tippedToday = new HashSet<string>();
        var tippedLastHour = new HashSet<string>();
        var blacklist = new HashSet<string> { "Player1" };

        var result = AutotipServiceTestHelper.SelectBoosterFromCandidates(
            boosters, 
            boosterCounts, 
            tippedToday, 
            tippedLastHour, 
            blacklist
        );

        Assert.That(result, Is.EqualTo("Player2"));
    }

    [Test]
    public void SelectBooster_ReturnsNull_WhenNoCandidates()
    {
        var boosters = new List<ActiveBooster>
        {
            new() { purchaserName = "Player1", gamemode = "arcade" }
        };

        var boosterCounts = new Dictionary<string, int>
        {
            { "Player1", 1 }
        };

        var tippedToday = new HashSet<string> { "Player1" };
        var tippedLastHour = new HashSet<string>();
        var blacklist = new HashSet<string>();

        var result = AutotipServiceTestHelper.SelectBoosterFromCandidates(
            boosters, 
            boosterCounts, 
            tippedToday, 
            tippedLastHour, 
            blacklist
        );

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetBoosterCountsByPlayer_CountsCorrectly()
    {
        var allBoosters = new Dictionary<string, List<ActiveBooster>>
        {
            ["arcade"] = new()
            {
                new() { purchaserName = "Player1" },
                new() { purchaserName = "Player2" }
            },
            ["skywars"] = new()
            {
                new() { purchaserName = "Player2" },
                new() { purchaserName = "Player3" }
            },
            ["blitz"] = new()
            {
                new() { purchaserName = "Player2" }
            }
        };

        var result = AutotipServiceTestHelper.GetBoosterCountsByPlayer(allBoosters);

        Assert.That(result["Player1"], Is.EqualTo(1));
        Assert.That(result["Player2"], Is.EqualTo(3));
        Assert.That(result["Player3"], Is.EqualTo(1));
    }

    [Test]
    public void FilterBoostersTippedInLastHour_FiltersCorrectly()
    {
        var activeBoosterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Booster1", "Booster2", "Booster3"
        };

        var recentTips = new List<AutotipEntry>
        {
            new() { TippedPlayerName = "Booster1", TippedAt = DateTimeOffset.UtcNow.AddMinutes(-30) },
            new() { TippedPlayerName = "Booster2", TippedAt = DateTimeOffset.UtcNow.AddMinutes(-90) },
            new() { TippedPlayerName = "RegularPlayer", TippedAt = DateTimeOffset.UtcNow.AddMinutes(-30) }
        };

        var result = AutotipServiceTestHelper.FilterBoostersTippedInLastHour(recentTips, activeBoosterNames);

        Assert.That(result, Does.Contain("Booster1"));
        Assert.That(result, Does.Not.Contain("Booster2")); // Tipped more than an hour ago
        Assert.That(result, Does.Not.Contain("RegularPlayer")); // Not a booster
        Assert.That(result, Does.Not.Contain("Booster3")); // Not tipped
    }
}

/// <summary>
/// Helper class to expose internal logic for testing
/// </summary>
public static class AutotipServiceTestHelper
{
    public static string FindGamemodeNeedingTip(string userId, List<AutotipEntry> recentTips)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddMinutes(-30);

            var lastTipByGamemode = new Dictionary<string, DateTimeOffset>();

            foreach (var gm in AutotipService.SupportedGamemodes)
            {
                lastTipByGamemode[gm.ToLowerInvariant()] = DateTimeOffset.MinValue;
            }

            foreach (var tip in recentTips)
            {
                if (string.IsNullOrEmpty(tip.Gamemode))
                    continue;

                var gm = tip.Gamemode.ToLowerInvariant();
                if (!lastTipByGamemode.ContainsKey(gm))
                    continue;

                if (tip.TippedAt > lastTipByGamemode[gm])
                    lastTipByGamemode[gm] = tip.TippedAt;
            }

            var candidates = lastTipByGamemode
                .Where(kvp => kvp.Value <= cutoff)
                .OrderBy(kvp => kvp.Value)
                .ToList();

            if (candidates.Count == 0)
                return null;

            var chosenKey = candidates.First().Key;
            return AutotipService.SupportedGamemodes.FirstOrDefault(g => g.ToLowerInvariant() == chosenKey);
        }
        catch
        {
            return null;
        }
    }

    public static string SelectBoosterFromCandidates(
        List<ActiveBooster> boosters,
        Dictionary<string, int> boosterCounts,
        HashSet<string> tippedTodayNames,
        HashSet<string> tippedBoostersLastHour,
        HashSet<string> blacklist)
    {
        var candidates = boosters
            .Where(b => !string.IsNullOrEmpty(b.purchaserName)
                && !blacklist.Contains(b.purchaserName)
                && !tippedTodayNames.Contains(b.purchaserName)
                && !tippedBoostersLastHour.Contains(b.purchaserName))
            .ToList();

        if (candidates.Count == 0)
            return null;

        var sortedCandidates = candidates
            .OrderByDescending(b => boosterCounts.TryGetValue(b.purchaserName, out var count) ? count : 0)
            .ToList();

        return sortedCandidates.First().purchaserName;
    }

    public static Dictionary<string, int> GetBoosterCountsByPlayer(Dictionary<string, List<ActiveBooster>> allBoosters)
    {
        return allBoosters.Values
            .SelectMany(boosters => boosters)
            .Where(b => !string.IsNullOrEmpty(b.purchaserName))
            .GroupBy(b => b.purchaserName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
    }

    public static HashSet<string> FilterBoostersTippedInLastHour(
        List<AutotipEntry> recentTips,
        HashSet<string> activeBoosterNames)
    {
        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        return recentTips
            .Where(t => t.TippedAt > oneHourAgo && activeBoosterNames.Contains(t.TippedPlayerName))
            .Select(t => t.TippedPlayerName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
