using System;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class ForgeTask : ProfitTask
{
    public override async Task<TaskResult> Execute(TaskParams parameters)
    {
        var forgeStatus = parameters.ExtractedInfo.ForgeItems;
        if (forgeStatus == null || forgeStatus.Count == 0)
        {
            return new TaskResult
            {
                ProfitPerHour = 0,
                Message = "The status of forge is unknown, if you have unlocked it, please open the forge menu and try again"
            };
        }
        if (forgeStatus.All(f => f.Tag != null && f.ForgeEnd > DateTime.UtcNow))
        {
            var forgeEnd = forgeStatus.Min(f => f.ForgeEnd);
            return new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"You have an active forge task, the next will be finished in {parameters.Socket.formatProvider.FormatTime(forgeEnd - DateTime.UtcNow)}."
            };
        }

        var flips = await ForgeCommand.GetPossibleFlips(parameters.Socket);
        var best = flips
            .Where(f => f.CraftData.CraftCost < 20_000_000_000 && f.ProfitPerHour > 0)
            .OrderByDescending(f => f.ProfitPerHour)
            .FirstOrDefault();

        if (best == null)
        {
            return new TaskResult
            {
                ProfitPerHour = 0,
                Message = "No profitable forge flips found, please try again later."
            };
        }

        return new TaskResult
        {
            ProfitPerHour = (int)best.ProfitPerHour,
            Message = $"Forge {best.CraftData.ItemName}, takes {parameters.Socket.formatProvider.FormatTime(TimeSpan.FromSeconds(best.Duration))}",
            Details = $"Ingredients required:\n" +
                      string.Join("\n", best.CraftData.Ingredients.Select(i => $"{i.ItemId} x{i.Count} ({parameters.Socket.FormatPrice(i.Cost)})"))
                      + $"\nClick to warp to forge",
            OnClick = $"/warp forge"
        };
    }

    public override string Description => "Calculates profit using the forge";
}