using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Base class for slayer tasks that tracks profit using player period data.
/// </summary>
public abstract class IndividualSlayerTask : ProfitTask
{
    protected abstract string SlayerName { get; }
    protected abstract HashSet<string> LocationNames { get; }

    public override Task<TaskResult> Execute(TaskParams parameters)
    {
        var matched = parameters.LocationProfit
            .Where(lp => LocationNames.Contains(lp.Key))
            .SelectMany(lp => lp.Value)
            .ToList();

        if (matched.Count == 0)
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"No {SlayerName} activity tracked so far.",
                Details = $"Do some {SlayerName} slayer runs so we can calculate your profitability."
            });

        var totalProfit = matched.Sum(p => (double)p.Profit);
        var totalHours = matched.Sum(p => (p.EndTime - p.StartTime).TotalHours);
        if (totalHours <= 0)
            return Task.FromResult(new TaskResult { ProfitPerHour = 0, Message = $"{SlayerName} sessions too short.", Name = SlayerName });

        var perHour = totalProfit / totalHours;
        var items = matched.SelectMany(p => p.ItemsCollected)
            .GroupBy(i => i.Key).ToDictionary(g => g.Key, g => (long)g.Sum(v => v.Value))
            .OrderByDescending(i => i.Value);
        var formattedDuration = parameters.Socket.formatProvider.FormatTime(TimeSpan.FromHours(totalHours));
        var itemBreakDown = items.Take(15)
            .Select(i => $"{McColorCodes.YELLOW}{i.Key} {McColorCodes.GRAY}x{i.Value}")
            .DefaultIfEmpty("No items tracked").Aggregate((a, b) => a + "\n" + b);

        return Task.FromResult(new TaskResult
        {
            ProfitPerHour = (int)perHour,
            Message = $"{SlayerName} earning {McColorCodes.AQUA}{parameters.Socket.FormatPrice((long)totalProfit)} {McColorCodes.GRAY}over {formattedDuration}.",
            Details = $"Time tracked: {formattedDuration}\nItems collected:\n{itemBreakDown}",
            Name = SlayerName
        });
    }
}

// ── Blaze Slayer ──
public class T4InfernoDemonlordTask : IndividualSlayerTask
{
    protected override string SlayerName => "T4 Inferno Demonlord";
    protected override HashSet<string> LocationNames => ["Stronghold", "Smoldering Tomb", "The Bastion", "Crimson Isle"];
    public override string Description => "T4 Inferno Demonlord (Blaze Slayer)";
}
public class T3InfernoDemonlordTask : IndividualSlayerTask
{
    protected override string SlayerName => "T3 Inferno Demonlord";
    protected override HashSet<string> LocationNames => ["Stronghold", "Smoldering Tomb", "The Bastion", "Crimson Isle"];
    public override string Description => "T3 Inferno Demonlord (Blaze Slayer)";
}

// ── Spider Slayer ──
public class T5TarantulaTask : IndividualSlayerTask
{
    protected override string SlayerName => "T5 Tarantula";
    protected override HashSet<string> LocationNames => ["Spider's Den", "The Spider's Den", "Arachne's Sanctuary", "Spider Mound"];
    public override string Description => "T5 Tarantula Broodfather";
}
public class T4TarantulaTask : IndividualSlayerTask
{
    protected override string SlayerName => "T4 Tarantula";
    protected override HashSet<string> LocationNames => ["Spider's Den", "The Spider's Den", "Arachne's Sanctuary", "Spider Mound"];
    public override string Description => "T4 Tarantula Broodfather";
}

// ── Crimson Isle bosses ──
public class AshfangTask : IndividualSlayerTask
{
    protected override string SlayerName => "Ashfang";
    protected override HashSet<string> LocationNames => ["Ruins of Ashfang", "Smoldering Tomb", "Blazing Volcano"];
    public override string Description => "Ashfang on Crimson Isle";
}
public class BarbarianDukeXTask : IndividualSlayerTask
{
    protected override string SlayerName => "Barbarian Duke X";
    protected override HashSet<string> LocationNames => ["The Dukedom", "Stronghold", "Dragontail", "Mage Outpost"];
    public override string Description => "Barbarian Duke X on Crimson Isle";
}

// ── Enderman Slayer ──
public class T4VoidgloomsTask : MethodTask
{
    protected override string MethodName => "T4 Voidglooms";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["NULL_SPHERE", "SUMMONING_EYE"];
    protected override List<MethodDrop> FormulaDrops => [new("NULL_SPHERE", 15), new("SUMMONING_EYE", 2)];
}
public class T4VoidgloomsFdTask : MethodTask
{
    protected override string MethodName => "T4 Voidglooms (FD)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["NULL_SPHERE", "SUMMONING_EYE"];
    protected override List<MethodDrop> FormulaDrops => [new("NULL_SPHERE", 20), new("SUMMONING_EYE", 3)];
}
