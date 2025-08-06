using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

public abstract class IslandTask : ProfitTask
{
    protected abstract string RegionName { get; }
    protected abstract HashSet<string> locationNames { get; }

    public virtual bool IsPossibleAt(DateTime time)
    {
        // Default implementation assumes the task is always possible
        return true;
    }

    public override Task<TaskResult> Execute(TaskParams parameters)
    {
        var locations = parameters.LocationProfit
            .Where(l => locationNames.Contains(l.Key))
            .Select(l => (data: l.Value,
                totalProfit: l.Value.Sum(l => l.Profit),
                totalTime: TimeSpan.FromHours(l.Value.Sum(l => (l.EndTime - l.StartTime).TotalHours)),
                perHour: l.Value.Sum(l => l.Profit) / l.Value.Sum(l => (l.EndTime - l.StartTime).TotalHours)))
            .OrderByDescending(l => l.totalTime < TimeSpan.FromMinutes(1) ? l.perHour / 100 : l.perHour)
            .ToList();
        if (locations.Count == 0)
        {
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"No {Name} activity tracked so far.",
                Details = $"Please do {RegionName} island \nand do some activities \nso we can calculate the profitability."
            });
        }
        if (!IsPossibleAt(parameters.TestTime))
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"The {RegionName} island/task is not possible at this time.",
                Details = "Its time locked in some way"
            });
        var bestLocation = locations.First();
        var totalTime = locations.Sum(l => l.data.Sum(d => (d.EndTime - d.StartTime).TotalHours));
        var formattedDuration = parameters.Socket.formatProvider.FormatTime(TimeSpan.FromHours(totalTime));
        var items = locations.SelectMany(l=>l.data).SelectMany(i => i.ItemsCollected)
            .GroupBy(i => i.Key, i => i.Value)
            .ToDictionary(g => g.Key, g => g.Sum())
            .OrderByDescending(i => i.Value);
        var itemBreakDown = items
            .Take(20)
            .Select(i => $"{McColorCodes.YELLOW}{i.Key} {McColorCodes.GRAY}x{i.Value}")
            .Aggregate((a, b) => a + "\n" + b);
        var totalProfit = locations.Sum(l => l.totalProfit);
        var perHour = totalProfit / totalTime;
        var itemCount = items.Where(i => i.Value > 0).Sum(i => i.Value);
        return Task.FromResult(new TaskResult
        {
            ProfitPerHour = (int)perHour,
            Message = $"{Name} with {McColorCodes.AQUA}{parameters.Socket.FormatPrice(totalProfit)} {McColorCodes.GRAY}with {McColorCodes.GREEN}{itemCount} items {McColorCodes.GRAY}over {formattedDuration}.",
            Details = $"Total locations considered: {locations.Count}\n" +
                      $"Time tracked: {formattedDuration}\n"
                      + $"Items collected:\n{itemBreakDown}",
            OnClick = "/warp " + RegionName
        });
    }

    public override string Description => $"Calculates profit while being on {RegionName} island";
}