using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class KatTask : ProfitTask
{
    public record FlipData(KatUpgradeResult katData, PriceSumary sumary)
    {
    }
    public override async Task<TaskResult> Execute(TaskParams parameters)
    {
        var katData = await GetOrUpdateCache(parameters, async () =>
        {
            var all = await parameters.GetService<Crafts.Client.Api.IKatApi>().GetProfitableKatAsync();
            var top10 = all.Where(k => k.CoreData.Hours != 0).OrderByDescending(k => k.Profit / (k.CoreData.Hours + 0.1)).Skip(1).Take(10);
                    var cleanPrices = parameters.GetService<ISniperClient>().GetCleanPrices();
            var result = top10.Select((async i =>
            {
                try
                {
                    var sumary = parameters.CleanPrices.GetValueOrDefault(i.CoreData.ItemTag, 0);
                    return new FlipData(i, new() { Med = sumary, Volume = 2 });
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "getting price summary for kat flips");
                }
                return null;
            }));
            return (await Task.WhenAll(result)).ToList();
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
        var best = katData.Where(k => k.katData.CoreData.Hours != 0)
            .OrderByDescending(k => Math.Min(k.katData.Profit, k.sumary?.Med ?? k.katData.Profit) / (k.katData.CoreData.Hours / attributeMultiplier + 0.1) * k.sumary?.Volume ?? 1)
            .Skip(1).FirstOrDefault();
        var coreData = best.katData.CoreData;
        var profit = Math.Min(best.katData.Profit, best.sumary.Med);
        var hours = coreData.Hours / attributeMultiplier;
        var explanation = $"{McColorCodes.YELLOW}Kat's Favorite {McColorCodes.GRAY}level {attributeLevel} reduces the time to {McColorCodes.AQUA}{attributeMultiplier * 100}%"
            + $"\n{McColorCodes.GRAY}Upgrading {coreData.Name} with kat starting with {coreData.BaseRarity}, you will need:";
        if (coreData.Material != null)
            explanation += $"\n{McColorCodes.YELLOW}{coreData.Material} {McColorCodes.GRAY}x{coreData.Amount}";
        else
            explanation += $"\nno extra materials";
        if (coreData.Material2 != null)
            explanation += $"\n{McColorCodes.YELLOW}{coreData.Material2} {McColorCodes.GRAY}x{coreData.Amount2}";
        if (coreData.Material3 != null)
            explanation += $"\n{McColorCodes.YELLOW}{coreData.Material3} {McColorCodes.GRAY}x{coreData.Amount3}";
        if (coreData.Material4 != null)
            explanation += $"\n{McColorCodes.YELLOW}{coreData.Material4} {McColorCodes.GRAY}x{coreData.Amount4}";
        explanation += $"\nYou will spend {formatProvider.FormatPrice(best.katData.PurchaseCost)} to buy the auction \n{formatProvider.FormatPrice((long)best.katData.MaterialCost)} on materials "
           + $"and {formatProvider.FormatPrice((long)best.katData.UpgradeCost)} on kat"
           + $"\n{McColorCodes.YELLOW}Total time {McColorCodes.GRAY}{formatProvider.FormatTime(TimeSpan.FromHours(hours))}"
           + $"\n{McColorCodes.YELLOW}Total profit {McColorCodes.GRAY}{formatProvider.FormatPrice((long)profit)}";
        var feeAndRiskFactor = 0.97;
        return new TaskResult
        {
            ProfitPerHour = (int)(profit * feeAndRiskFactor / (coreData.Hours + 0.1) / attributeMultiplier),
            Message = $"Upgrade {coreData.Name} with kat starting with {coreData.BaseRarity}, click to get cheapest purchase, if its already sold try again in a minute.",
            Details = explanation,
            OnClick = $"/viewauction {best.katData.OriginAuction}"
        };
    }

    public override string Description => "Calculates profit from kat tasks if no pet is currently upgraded.";
}
