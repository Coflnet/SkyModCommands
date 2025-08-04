using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class KatTask : ProfitTask
{
    public override async Task<TaskResult> Execute(TaskParams parameters)
    {
        var katData = await GetOrUpdateCache(parameters, async () =>
        {
            return await parameters.GetService<Crafts.Client.Api.IKatApi>().GetProfitableKatAsync();
        }, 1);
        var formatProvider = parameters.Socket.formatProvider;
        var expireTime = parameters.ExtractedInfo.KatStatus?.KatEnd;
        if (expireTime == null)
        {
            return new TaskResult
            {
                ProfitPerHour = 0,
                Message = "Status of kat is unknown, please open the kat menu and try again",
                Details = "The status of kat can only be read if you\n"
                         + "click on the kat npc in the skyblock hub"
            };
        }
        if (expireTime > DateTime.UtcNow)
        {
            return new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"Your kat is currently busy with {parameters.ExtractedInfo.KatStatus.ItemName} for another {formatProvider.FormatTime(parameters.ExtractedInfo.KatStatus.KatEnd - DateTime.UtcNow)}.",
                Details = "Please wait until kat is done with the current task.\nWould you like to get a push notification when kat is done?",
            };
        }
        // skip top one as its usually quickly bought, add 6 minutes for getting materials and setup
        var attributeLevel = parameters.ExtractedInfo.AttributeLevel?.GetValueOrDefault("Kat's Favorite") ?? 0;
        var attributeMultiplier = 1 - (attributeLevel * 0.01f);
        var best = katData.Where(k => k.CoreData.Hours != 0).OrderByDescending(k => k.Profit / (k.CoreData.Hours / attributeMultiplier + 0.1)).Skip(1).FirstOrDefault();
        var hours = best.CoreData.Hours / attributeMultiplier;
        var explanation = $"{McColorCodes.YELLOW}Kat's Favorite {McColorCodes.GRAY}level {attributeLevel} reduces the time to {McColorCodes.AQUA}{attributeMultiplier * 100}%"
            + $"\n{McColorCodes.GRAY}Upgrading {best.CoreData.Name} with kat starting with {best.CoreData.BaseRarity}, you will need:";
        if (best.CoreData.Material != null)
            explanation += $"\n{McColorCodes.YELLOW}{best.CoreData.Material} {McColorCodes.GRAY}x{best.CoreData.Amount}";
        else
            explanation += $"\nno extra materials";
        if (best.CoreData.Material2 != null)
            explanation += $"\n{McColorCodes.YELLOW}{best.CoreData.Material2} {McColorCodes.GRAY}x{best.CoreData.Amount2}";
        if (best.CoreData.Material3 != null)
            explanation += $"\n{McColorCodes.YELLOW}{best.CoreData.Material3} {McColorCodes.GRAY}x{best.CoreData.Amount3}";
        if (best.CoreData.Material4 != null)
            explanation += $"\n{McColorCodes.YELLOW}{best.CoreData.Material4} {McColorCodes.GRAY}x{best.CoreData.Amount4}";
        explanation += $"\nYou will spend {formatProvider.FormatPrice(best.PurchaseCost)} to buy the auction \n{formatProvider.FormatPrice((long)best.MaterialCost)} on materials "
           + $"and {formatProvider.FormatPrice((long)best.UpgradeCost)} on kat"
           + $"\n{McColorCodes.YELLOW}Total time {McColorCodes.GRAY}{formatProvider.FormatTime(TimeSpan.FromHours(hours))}"
           + $"\n{McColorCodes.YELLOW}Total profit {McColorCodes.GRAY}{formatProvider.FormatPrice((long)best.Profit)}";
        return new TaskResult
        {
            ProfitPerHour = (int)(best.Profit / best.CoreData.Hours / attributeMultiplier),
            Message = $"Upgrade {best.CoreData.Name} with kat starting with {best.CoreData.BaseRarity}, click to get cheapest purchase, if its already sold try again in a minute.",
            Details = explanation,
            OnClick = $"/viewauction {best.OriginAuction}"
        };
    }

    public override string Description => "Calculates profit from kat tasks if no pet is currently upgraded.";
}
