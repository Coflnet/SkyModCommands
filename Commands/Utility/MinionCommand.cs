using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Sniper.Client.Api;

namespace Coflnet.Sky.Commands.MC;

public class MinionCommand : ReadOnlyListCommand<MinionService.MinionEffect>
{
    protected override string Title => "Top Minions by profit";
    protected override void Format(MinecraftSocket socket, DialogBuilder db, MinionService.MinionEffect elem)
    {
        db.MsgLine($" {elem.name} {McColorCodes.RED}{socket.FormatPrice(elem.craftCostEst)}/minion {McColorCodes.GRAY}is {McColorCodes.AQUA}{socket.FormatPrice(elem.profitPerDay)} per day");
    }

    protected override async Task<IEnumerable<MinionService.MinionEffect>> GetElements(MinecraftSocket socket, string val)
    {
        var bazaarApi = socket.GetService<IBazaarApi>();
        var sniper = socket.GetService<ISniperApi>();
        var prices = new Dictionary<string, double>();
        var sniperTask = sniper.ApiSniperPricesCleanGetAsync();
        var bazaar = await bazaarApi.GetAllPricesAsync();
        foreach (var item in bazaar)
        {
            prices[item.ProductId] = item.BuyPrice;
        }
        foreach (var item in await sniperTask)
        {
            prices[item.Key] = item.Value;
        }
        var minionService = socket.GetService<MinionService>();
        return minionService.GetCurrentEffects(prices).OrderByDescending(p=>p.profitPerDay).ToList();
    }

    protected override string GetId(MinionService.MinionEffect elem)
    {
        return elem.name;
    }
}
