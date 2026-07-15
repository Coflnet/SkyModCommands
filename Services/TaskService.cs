using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Commands.MC.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Shared singleton that holds the task registry and executes tasks.
/// Used by both the WebSocket TaskCommand and the REST TaskController.
/// </summary>
public class TaskService
{
    private readonly List<ProfitTask> _tasks;

    public TaskService()
    {
        _tasks = TaskCatalog.Create().Values.Distinct().ToList();
    }

    /// <summary>
    /// Returns all registered task instances.
    /// </summary>
    public IReadOnlyList<ProfitTask> Tasks => _tasks;

    /// <summary>
    /// Execute all tasks against the given parameters and return results sorted by profit.
    /// </summary>
    public async Task<List<TaskResult>> ExecuteAll(TaskParams parameters)
    {
        var all = await System.Threading.Tasks.Task.WhenAll(_tasks.Select(async task =>
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
                    Message = $"Error calculating {task.Name}",
                    Details = e.ToString()
                };
            }
        }));
        return all.OrderByDescending(r => r.ProfitPerHour).ToList();
    }

    /// <summary>
    /// Returns metadata for all registered MethodTask instances (no execution needed).
    /// </summary>
    public List<MethodMetadata> GetMethodMetadata()
    {
        return _tasks.OfType<MethodTask>().Select(t => new MethodMetadata
        {
            Name = t.Name,
            Description = t.Description,
        }).ToList();
    }

    /// <summary>
    /// Community-aggregated average drop rates per method.
    /// Updated externally (e.g. by a background service or API call).
    /// </summary>
    private ConcurrentDictionary<string, List<AverageDrop>> _globalAverages = new();

    /// <summary>
    /// Returns a snapshot of the current global average drop rates for all methods.
    /// </summary>
    public Dictionary<string, List<AverageDrop>> GetGlobalAverages() => new(_globalAverages);

    /// <summary>
    /// Aggregate drop rates from a player's periods and merge into the global averages.
    /// Uses exponential moving average to weight recent data more heavily.
    /// </summary>
    public void UpdateGlobalAverages(Dictionary<string, Period[]> locationProfit)
    {
        foreach (var methodTask in _tasks.OfType<MethodTask>())
        {
            var fakeParams = new TaskParams { LocationProfit = locationProfit, TestTime = DateTime.UtcNow };
            var periods = methodTask.FindMatchingPeriodsForAggregation(fakeParams);
            if (periods.Count == 0) continue;

            var totalHours = periods.Sum(p => (p.EndTime - p.StartTime).TotalHours);
            if (totalHours < 1.0 / 60) continue; // Need at least 1 minute of data

            var itemRates = periods
                .Where(p => p.ItemsCollected != null)
                .SelectMany(p => p.ItemsCollected)
                .GroupBy(i => i.Key)
                .Select(g => new AverageDrop(g.Key, g.Sum(v => v.Value) / totalHours, 1))
                .ToList();

            _globalAverages.AddOrUpdate(
                methodTask.Name,
                itemRates,
                (_, existing) => MergeAverages(existing, itemRates));
        }
    }

    private static List<AverageDrop> MergeAverages(List<AverageDrop> existing, List<AverageDrop> newData)
    {
        var merged = new Dictionary<string, AverageDrop>();
        foreach (var drop in existing)
            merged[drop.ItemTag] = drop;

        foreach (var drop in newData)
        {
            if (merged.TryGetValue(drop.ItemTag, out var prev))
            {
                var totalSamples = prev.SampleCount + 1;
                // Weighted average: give less weight to each subsequent sample to be robust against outliers
                var newRate = (prev.RatePerHour * prev.SampleCount + drop.RatePerHour) / totalSamples;
                merged[drop.ItemTag] = new AverageDrop(drop.ItemTag, newRate, totalSamples);
            }
            else
            {
                merged[drop.ItemTag] = drop;
            }
        }
        return merged.Values.ToList();
    }
}

public class MethodMetadata
{
    public string Name { get; set; }
    public string Description { get; set; }
}
