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
            .Select(l => (data: l.Value, perHour: l.Value.Profit / (l.Value.EndTime - l.Value.StartTime).TotalHours))
            .OrderByDescending(l => l.perHour)
            .ToList();
        if (locations.Count == 0)
        {
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"No {RegionName} activity tracked so far.",
                Details = $"Please visit {RegionName} island \nand do some activities \nso we can calculate the profitability."
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
        var formattedDuration = parameters.Socket.formatProvider.FormatTime(bestLocation.data.EndTime - bestLocation.data.StartTime);
        var itemBreakDown = bestLocation.data.ItemsCollected
            .OrderByDescending(i => i.Value)
            .Take(3)
            .Select(i => $"{i.Key} {McColorCodes.GRAY}x{i.Value}")
            .Aggregate((a, b) => a + "\n" + b);
        return Task.FromResult(new TaskResult
        {
            ProfitPerHour = (int)bestLocation.perHour,
            Message = $"{RegionName}: {McColorCodes.AQUA}{bestLocation.data.Location} with {parameters.Socket.FormatPrice(bestLocation.data.Profit)} over {formattedDuration}.",
            Details = $"Total locations considered: {locations.Count}\n" +
                      $"Best start: {bestLocation.data.StartTime:f}\n" +
                      $"Duration: {formattedDuration}\n"
                      + $"Items collected:\n{itemBreakDown}",
            OnClick = "/warp " + RegionName
        });
    }

    public override string Description => $"Calculates profit while being on {RegionName} island";
}