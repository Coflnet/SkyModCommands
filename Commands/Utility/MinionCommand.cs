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
    public MinionCommand()
    {
        sorters.Add("cost", list => list.OrderByDescending(e => e.craftCostEst));
        sorters.Add("nbc", list => list.OrderByDescending(e => e.nbcSellPerDay));
        sorters.Add("cheapest", list => list.OrderBy(e => e.craftCostEst));
    }
    protected override void Format(MinecraftSocket socket, DialogBuilder db, MinionService.MinionEffect elem)
    {
        db.MsgLine($" {elem.name} {McColorCodes.RED}{socket.FormatPrice(elem.craftCostEst)}/minion {McColorCodes.GRAY}is {McColorCodes.AQUA}{socket.FormatPrice(elem.profitPerDay)} per day",
            "https://hypixel-skyblock.fandom.com/wiki/" + elem.name.Replace(" ", "_"),
            $"Profit per day: {McColorCodes.AQUA}{socket.FormatPrice(elem.profitPerDay)}\n" +
            $"Estimated cost to craft: {McColorCodes.RED}{socket.FormatPrice(elem.craftCostEst)}\n" +
            $"Estimated NPC sell per day: {McColorCodes.GOLD}{socket.FormatPrice(elem.nbcSellPerDay)}\n" +
            $"Delay per action: {McColorCodes.YELLOW}{elem.minionData.TierDelay.Last()} seconds\n" +
            $"Products: {McColorCodes.GREEN}{string.Join(", ", elem.minionData.Products.Where(p => p.Tag != null).Select(p => p.Tag))}\n" +
            $"{McColorCodes.YELLOW}Click to view on the Skyblock Wiki");
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
