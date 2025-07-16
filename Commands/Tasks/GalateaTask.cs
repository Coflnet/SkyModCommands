using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class GalateaTask : ProfitTask
{
    private readonly HashSet<string> galateaLocationNames =
    [
        "Ancient Ruins",
        "Evergreen Plateau",
        "Dive-Ember Pass",
        "Driptoad Delve",
        "Drowned Reliquary",
        "Fusion House",
        "Kelpwoven Tunnels",
        "Moonglade Marsh",
        "Murkwater Depths",
        "Murkwater Shallows",
        "Murkwater Loch",
        "Murkwater Outpost",
        "North Wetlands",
        "North Reaches",
        "Red House",
        "Reafguard Pass",
        "Side-Ember Way",
        "South Reaches",
        "South Wetlands",
        "Stride-Ember Fissure",
        "Squid Cave",
        "Tangleburg",
        "Tangleburg Bank",
        "Tangleburg's Path",
        "Tomb Floodway",
        "Tranquil Pass",
        "Tranquility Sanctum",
        "Verdant Summit",
        "West Reaches",
        "Wyrmgrove Tomb"
    ];

    public override Task<TaskResult> Execute(TaskParams parameters)
    {
        var locations = parameters.LocationProfit
            .Where(l => galateaLocationNames.Contains(l.Key))
            .Select(l => (data:l.Value, perHour: l.Value.Profit / (l.Value.EndTime - l.Value.StartTime).TotalHours))
            .OrderByDescending(l => l.perHour)
            .ToList();
        if (locations.Count == 0)
        {   
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Message = "No galatea activity tracked so far.",
                Details = "Please visit galatea island and do some activities so we can calculate the profitability."
            });
        }
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
            Message = $"Galatea: {McColorCodes.AQUA}{bestLocation.data.Location} with {parameters.Socket.FormatPrice(bestLocation.data.Profit)} over {formattedDuration}.",
            Details = $"Total locations considered: {locations.Count}\n" +
                      $"Best start: {bestLocation.data.StartTime:f}\n" +
                      $"Duration: {formattedDuration}\n"
                      + $"Items collected:\n{itemBreakDown}",
            OnClick = "/warp galatea"
        });
    }

    public override string Description => "Calculates profit while being on galatea island";
}