using System;
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
        var expireTime = parameters.ExtractedInfo.KatStatus?.KatEnd;
        if (expireTime == null)
        {
            return new TaskResult
            {
                ProfitPerHour = 0,
                Message = "Status of kat is unknown, please open the kat menu and try again"
            };
        }
        if (expireTime > DateTime.UtcNow)
        {
            return new TaskResult
            {
                ProfitPerHour = 0,
                Message = $"Your kat is currently busy with {parameters.ExtractedInfo.KatStatus.ItemName} for another {parameters.ExtractedInfo.KatStatus.KatEnd - DateTime.UtcNow}."
            };
        }
        // skip top one as its usually quickly bought
        var best = katData.Where(k=>k.CoreData.Hours != 0).OrderByDescending(k => k.Profit / k.CoreData.Hours).Skip(1).FirstOrDefault();
        return new TaskResult
        {
            ProfitPerHour = (int)(best.Profit / best.CoreData.Hours),
            Message = $"Upgrade {best.CoreData.Name} with kat starting with {best.CoreData.BaseRarity}, click to get cheapest purchase, if its already sold try again in a minute.",
            OnClick = $"/viewauction {best.OriginAuction}"
        };
    }

    public override string Description => "Calculates profit from kat tasks if no pet is currently upgraded.";
}
