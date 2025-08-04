using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class ComposterTask : ProfitTask
{
    public override async Task<TaskResult> Execute(TaskParams parameters)
    {
        var composterService = parameters.GetService<ComposterService>();
        var all = parameters.GetPrices();
        var compostPrice = all.GetValueOrDefault("COMPOST");
        var upgrades = parameters.ExtractedInfo?.Composter ?? new();
        var caclucated = composterService.GetBestFlip(parameters.GetPrices(), compostPrice, upgrades);
        var cropName = parameters.Names.GetValueOrDefault(caclucated.cropMatter, caclucated.cropMatter);
        var fuelName = parameters.Names.GetValueOrDefault(caclucated.fuel, caclucated.fuel);
        return new TaskResult()
        {
            ProfitPerHour = (int)caclucated.profitPerHour,
            Message = $"Fill your composter with {McColorCodes.YELLOW}{cropName} {McColorCodes.GRAY}and {McColorCodes.YELLOW}{fuelName}",
            OnClick = $"/bz {cropName}",
            Details = $"{McColorCodes.YELLOW}Click to open {cropName} on bazaar\n"
                + $"{McColorCodes.GRAY}Then buy {McColorCodes.AQUA}{fuelName} {McColorCodes.GRAY}afterwards and fill your composter\n"
                + $"When its done composting sell order on bazaar for top order\n"
                + $"{McColorCodes.GRAY}This accounts for your {McColorCodes.AQUA}{upgrades.CostReductionPercent}% cost reduction\n"
                + $"{McColorCodes.GRAY}and {McColorCodes.AQUA}{upgrades.MultiDropChance}% extra drop chance\n"
                + $"{McColorCodes.GRAY}and {McColorCodes.AQUA}{upgrades.SpeedPercentIncrease}% speed increase\n"

        };
    }
    public override string Description => "Composter on the garden island.";
}
