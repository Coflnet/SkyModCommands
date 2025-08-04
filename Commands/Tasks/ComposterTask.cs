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
        var caclucated = composterService.GetBestFlip(parameters.GetPrices(), compostPrice, parameters.ExtractedInfo?.Composter ?? new());
        var cropName = parameters.Names.GetValueOrDefault(caclucated.cropMatter, caclucated.cropMatter);
        var fuelName = parameters.Names.GetValueOrDefault(caclucated.fuel, caclucated.fuel);
        return new TaskResult()
        {
            ProfitPerHour = (int)caclucated.profitPerHour,
            Message = $"Fill your composter with {McColorCodes.YELLOW}{cropName} {McColorCodes.GRAY}and {McColorCodes.YELLOW}{fuelName}",
            OnClick = $"/bz {cropName}",
            Details = $"{McColorCodes.YELLOW}Click to open {cropName} on bazaar\n"
                + $"Then buy {fuelName} afterwards and fill your composter\n"
                + $"When its done composting sell order on bazaar for top order"
        };
    }
    public override string Description => "Composter on the garden island.";
}
