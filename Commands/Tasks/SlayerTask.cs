using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Aggregates recent profit sessions that are typical for Slayer activities across all islands
/// and recommends the most profitable Slayer to do right now.
/// </summary>
public class SlayerTask : ProfitTask
{
    public override string Description => "Do the most profitable Slayer based on your recent sessions";

    private static readonly Dictionary<string, HashSet<string>> SlayerLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        // Revenant Horror (Zombie) — Hub (Crypts/Graveyard)
        ["Revenant"] = new HashSet<string>(new[]
        {
            "Crypts", "Graveyard", "Ruins", "Coal Mine", "Mountain" // include nearby hub areas as safety
        }, StringComparer.OrdinalIgnoreCase),

        // Tarantula Broodfather (Spider) — Spider's Den
        ["Tarantula"] = new HashSet<string>(new[]
        {
            "The Spider's Den", "Spider's Den", "Arachne's Sanctuary", "Spider Mound"
        }, StringComparer.OrdinalIgnoreCase),

        // Sven Packmaster (Wolf) — The Park
        ["Sven"] = new HashSet<string>(new[]
        {
            "The Park", "Howling Cave", "The Howling Cave", "The Wolf's Den"
        }, StringComparer.OrdinalIgnoreCase),

        // Voidgloom Seraph (Enderman) — The End
        ["Voidgloom"] = new HashSet<string>(new[]
        {
            "The End", "Dragon's Nest", "Void Sepulture", "Void Slate", "Zealot Bruiser Hideout"
        }, StringComparer.OrdinalIgnoreCase),

        // Inferno Demonlord (Blaze) — Crimson Isle
        ["Inferno"] = new HashSet<string>(new[]
        {
            "Crimson Isle", "Stronghold", "Smoldering Tomb", "The Bastion"
        }, StringComparer.OrdinalIgnoreCase),

        // Vampire Slayer — The Rift
        ["Vampire"] = new HashSet<string>(new[]
        {
            "The Rift", "Stillgore Château", "West Village", "Wyld Woods", "Enigma's Crib"
        }, StringComparer.OrdinalIgnoreCase)
    };

    public override Task<TaskResult> Execute(TaskParams parameters)
    {
        // Aggregate recent periods by slayer bucket
        var results = new List<(string name, double perHour, double hours, Dictionary<string, long> items)>();

        foreach (var kv in SlayerLocations)
        {
            var slayer = kv.Key;
            var locations = kv.Value;

            var matched = parameters.LocationProfit
                .Where(lp => locations.Contains(lp.Key))
                .SelectMany(lp => lp.Value)
                .ToList();

            if (matched.Count == 0)
                continue;

            var totalProfit = matched.Sum(p => (double)p.Profit);
            var totalHours = matched.Sum(p => (p.EndTime - p.StartTime).TotalHours);
            if (totalHours <= 0)
                continue;

            var perHour = totalProfit / totalHours;
            // aggregate items
            var items = matched
                .SelectMany(p => p.ItemsCollected)
                .GroupBy(i => i.Key)
                .ToDictionary(g => g.Key, g => (long)g.Sum(v => v.Value));

            results.Add((slayer, perHour, totalHours, items));
        }

        if (results.Count == 0)
        {
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Message = "No Slayer activity tracked so far.",
                Details = "Start a few Slayer bosses so we can estimate your profit per hour."
            });
        }

        // pick best by per-hour (favor sessions with > 10 minutes total time)
        var best = results
            .OrderByDescending(r => r.hours < (10.0 / 60.0) ? r.perHour / 100.0 : r.perHour)
            .First();

        // show top item contributions
        var displayNames = parameters.Names ?? new Dictionary<string, string>();
        var topItems = best.items
            .Where(kv => kv.Value != 0)
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .Take(6)
            .Select(kv => $"§e{(displayNames.TryGetValue(kv.Key, out var n) ? n : kv.Key)} §7x{kv.Value}");

        return Task.FromResult(new TaskResult
        {
            ProfitPerHour = (int)Math.Round(best.perHour),
            Message = $"Slayer: {best.name} is currently your best ({parameters.Socket.FormatPrice((long)best.perHour)} /h)",
            Details = string.Join("\n", topItems),
            OnClick = null,
            MostlyPassive = false,
            Name = $"Slayer {best.name}"
        });
    }
}
