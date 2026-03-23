using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Represents an expected item drop for formula-based profit estimation
/// </summary>
public record MethodDrop(string ItemTag, double RatePerHour);

/// <summary>
/// Base class for method-specific profit tasks that detect which activity
/// the player is doing based on items collected and location.
/// Falls back to formula-based estimation when no player data exists.
/// </summary>
public abstract class MethodTask : ProfitTask
{
    protected abstract string MethodName { get; }
    protected virtual HashSet<string> Locations => [];
    /// <summary>
    /// Items that must be present to attribute a period to this method.
    /// If empty, matching is location-only (plus shard flags).
    /// </summary>
    protected virtual HashSet<string> DetectionItems => [];
    /// <summary>
    /// When true, periods with any SHARD_ item are excluded.
    /// Used for non-hunting fishing variants.
    /// </summary>
    protected virtual bool ExcludeShardItems => false;
    /// <summary>
    /// When true, periods must contain at least one SHARD_ item.
    /// Used for hunting fishing variants where specific shard is unknown.
    /// </summary>
    protected virtual bool RequireShardItems => false;
    /// <summary>
    /// Expected item drops per hour for formula-based estimation
    /// when the player has no tracked data.
    /// </summary>
    protected virtual List<MethodDrop> FormulaDrops => [];
    protected virtual string WarpCommand => null;
    /// <summary>
    /// Preferred time window in hours for averaging.
    /// Data is searched within this window first, then extended
    /// progressively up to 96h if too few samples exist.
    /// </summary>
    protected virtual double PreferredWindowHours => 3;

    public override string Description => $"Calculates profit for {MethodName}";

    public override Task<TaskResult> Execute(TaskParams parameters)
    {
        var matchedPeriods = FindMatchingPeriodsWindowed(parameters);

        if (matchedPeriods.Count > 0)
            return ComputeFromPlayerData(parameters, matchedPeriods);

        if (FormulaDrops.Count > 0)
            return ComputeFromFormula(parameters);

        return Task.FromResult(new TaskResult
        {
            ProfitPerHour = 0,
            Message = $"No {MethodName} data tracked yet.",
            Details = $"Do some {MethodName} so we can calculate profit.",
            Name = MethodName
        });
    }

    /// <summary>
    /// Find matching periods using progressive time windows.
    /// Starts at PreferredWindowHours and extends to 96h if insufficient data.
    /// </summary>
    protected List<Period> FindMatchingPeriodsWindowed(TaskParams parameters)
    {
        var allMatched = FindMatchingPeriods(parameters);
        if (allMatched.Count == 0)
            return allMatched;

        var now = parameters.TestTime;
        double[] windows = [PreferredWindowHours, 12, 24, 48, 96];

        foreach (var windowHours in windows)
        {
            var cutoff = now.AddHours(-windowHours);
            var inWindow = allMatched.Where(p => p.EndTime >= cutoff).ToList();
            var totalHours = inWindow.Sum(p => (p.EndTime - p.StartTime).TotalHours);
            // Need at least 1 period and 5 minutes of data
            if (inWindow.Count >= 1 && totalHours >= 5.0 / 60)
                return inWindow;
        }

        // Fall back to all available data
        return allMatched;
    }

    protected List<Period> FindMatchingPeriods(TaskParams parameters)
    {
        IEnumerable<Period> candidates = Locations.Count > 0
            ? parameters.LocationProfit
                .Where(lp => Locations.Contains(lp.Key))
                .SelectMany(lp => lp.Value)
            : parameters.LocationProfit.SelectMany(lp => lp.Value);

        if (DetectionItems.Count > 0)
            candidates = candidates.Where(p =>
                p.ItemsCollected != null &&
                p.ItemsCollected.Keys.Any(k => DetectionItems.Contains(k)));

        if (RequireShardItems)
            candidates = candidates.Where(p =>
                p.ItemsCollected != null &&
                p.ItemsCollected.Keys.Any(k => k.StartsWith("SHARD_")));

        if (ExcludeShardItems)
            candidates = candidates.Where(p =>
                p.ItemsCollected == null ||
                !p.ItemsCollected.Keys.Any(k => k.StartsWith("SHARD_")));

        return candidates.ToList();
    }

    protected Task<TaskResult> ComputeFromPlayerData(TaskParams parameters, List<Period> periods)
    {
        var totalProfit = periods.Sum(p => (double)p.Profit);
        var totalHours = periods.Sum(p => (p.EndTime - p.StartTime).TotalHours);

        if (totalHours <= 0)
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"{MethodName} sessions too short to estimate.",
                Name = MethodName
            });

        var perHour = totalProfit / totalHours;
        var items = periods
            .Where(p => p.ItemsCollected != null)
            .SelectMany(p => p.ItemsCollected)
            .GroupBy(i => i.Key)
            .ToDictionary(g => g.Key, g => (long)g.Sum(v => v.Value))
            .OrderByDescending(i => i.Value);

        var formattedDuration = parameters.Socket.formatProvider.FormatTime(TimeSpan.FromHours(totalHours));
        var itemCount = items.Where(i => i.Value > 0).Sum(i => i.Value);
        var itemBreakDown = items.Take(20)
            .Select(i => $"{McColorCodes.YELLOW}{i.Key} {McColorCodes.GRAY}x{i.Value}")
            .DefaultIfEmpty("No items tracked")
            .Aggregate((a, b) => a + "\n" + b);

        return Task.FromResult(new TaskResult
        {
            ProfitPerHour = (int)perHour,
            Message = $"{MethodName} with {McColorCodes.AQUA}{parameters.Socket.FormatPrice(totalProfit)} {McColorCodes.GRAY}({McColorCodes.GREEN}{itemCount} items{McColorCodes.GRAY}) over {formattedDuration}.",
            Details = $"Time tracked: {formattedDuration}\nItems collected:\n{itemBreakDown}",
            Name = MethodName,
            OnClick = WarpCommand
        });
    }

    protected Task<TaskResult> ComputeFromFormula(TaskParams parameters)
    {
        var prices = parameters.GetPrices();
        var totalPerHour = 0.0;
        var breakdown = new List<string>();

        foreach (var drop in FormulaDrops)
        {
            var price = prices.GetValueOrDefault(drop.ItemTag, 0);
            if (price <= 0) continue;
            var contribution = drop.RatePerHour * price;
            totalPerHour += contribution;
            var name = parameters.Names.GetValueOrDefault(drop.ItemTag, drop.ItemTag);
            breakdown.Add($"{McColorCodes.YELLOW}{name} {McColorCodes.GRAY}x{drop.RatePerHour:F0}/h = {McColorCodes.AQUA}{parameters.Socket.FormatPrice((long)contribution)}");
        }

        if (totalPerHour <= 0)
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"{MethodName} - price data unavailable.",
                Name = MethodName
            });

        return Task.FromResult(new TaskResult
        {
            ProfitPerHour = (int)totalPerHour,
            Message = $"{MethodName} ~{McColorCodes.AQUA}{parameters.Socket.FormatPrice((long)totalPerHour)}/h {McColorCodes.GRAY}(estimated)",
            Details = $"Estimated drops per hour:\n{string.Join("\n", breakdown)}\n{McColorCodes.DARK_GRAY}(Do this method for personalized tracking)",
            Name = MethodName,
            OnClick = WarpCommand
        });
    }
}
