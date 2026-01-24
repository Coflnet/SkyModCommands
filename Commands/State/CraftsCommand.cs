using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Displays craft flips you can do.",
    "Based on unlocked collectionsAnd slayer level")]
public class CraftsCommand : ReadOnlyListCommand<ProfitableCraft>
{
    protected override string Title => $"Most profitable Craft Flips you can do (includes collection, skill and slayer level)";

    public override bool IsPublic => true;

    protected override string NoMatchText => "No profitable craft flips found currently :/";
    protected override bool Hidetop3WithoutPremium => true;

    private HashSet<string> OnBazaar = new HashSet<string>();

    public CraftsCommand()
    {
        sorters.Add("profit", (a) => a.OrderByDescending(f => FlipInstance.ProfitAfterFees((long)f.SellPrice, (long)f.CraftCost)));
        sorters.Add("order", (a) => a
            .Select(e =>
            {
                e.CraftCost = e.BuyOrderCraftCost;
                return e;
            }).OrderByDescending(f => FlipInstance.ProfitAfterFees((long)f.SellPrice, (long)f.BuyOrderCraftCost) * f.Volume));
        sorters.Add("cost", (a) => a.OrderByDescending(f => f.CraftCost));
        sorters.Add("volume", (a) => a.OrderByDescending(f => f.Volume));
        sorters.Add("percent", (a) => a.OrderByDescending(f => (f.SellPrice - f.CraftCost) / f.CraftCost));
        sorters.Add("median", (a) => a.OrderByDescending(f => f.Median - f.CraftCost));
        sorters.Add("bazaar", (a) => a.Where(a => OnBazaar.Contains(a.ItemId)).OrderByDescending(f => FlipInstance.ProfitAfterFees((long)f.SellPrice, (long)f.CraftCost)));
    }
    protected override void Format(MinecraftSocket socket, DialogBuilder db, ProfitableCraft elem)
    {
        var ingedientList = string.Join('\n', elem.Ingredients.Select(i => FormatIngredientText(socket, i)));
        var hoverText = $"{McColorCodes.GRAY}Ingredients for {elem.ItemName}:\n" + ingedientList
        + $"\n{McColorCodes.GRAY}CraftCost: {McColorCodes.GOLD}{socket.FormatPrice(elem.CraftCost)}"
        + $"\n{McColorCodes.GRAY}Volume: {McColorCodes.GREEN}{socket.FormatPrice(elem.Volume)}  {McColorCodes.YELLOW}Show recipe breakdown"
        + $"\n{McColorCodes.GRAY}Estimated Profit: {McColorCodes.AQUA}{socket.FormatPrice(FlipInstance.ProfitAfterFees((long)elem.SellPrice, (long)elem.CraftCost))}{McColorCodes.GRAY}(incl fees)";
        var click = $"/cofl recipe {elem.ItemId}";
        if (elem.ItemId == "???")
        {
            click = "/cofl buy starter_premium";
            hoverText = $"{McColorCodes.GRAY}You need starter premium or higher to see the top 3\n{McColorCodes.YELLOW}Click to buy a tier";
        }
        db.MsgLine($" {elem.ItemName} {McColorCodes.GRAY}for {McColorCodes.AQUA}{socket.FormatPrice(elem.Median)} {McColorCodes.YELLOW}[Open Recipe]", click, hoverText);
    }

    private static string FormatIngredientText(MinecraftSocket socket, Ingredient i)
    {
        if (i.Type == "craft")
            return $"{McColorCodes.YELLOW} craft {McColorCodes.GOLD}{i.ItemId} {McColorCodes.AQUA}x{i.Count} {McColorCodes.GRAY}cost ~{McColorCodes.GOLD}{socket.FormatPrice(i.Cost)}{McColorCodes.GRAY}(cheaper)";
        return $"{i.ItemId} {McColorCodes.AQUA}x{i.Count} {McColorCodes.GRAY}cost {McColorCodes.GOLD}{socket.FormatPrice(i.Cost)}";
    }

    protected override async Task<IEnumerable<ProfitableCraft>> GetElements(MinecraftSocket socket, string val)
    {
        var craftApi = socket.GetService<ICraftsApi>();
        var profileApi = socket.GetService<IProfileClient>();
        var craftsTask = NewMethod(craftApi);
        var filtered = (await profileApi.FilterProfitableCrafts(craftsTask, socket.SessionInfo.McUuid, "current"))
                .OrderByDescending(f => f.SellPrice - f.CraftCost);

        if (OnBazaar.Count == 0)
            _ = socket.TryAsyncTimes(async () =>
            {
                var itemsApi = socket.GetService<IItemsApi>();
                var bazaar = await itemsApi.ItemsBazaarTagsGetAsync();
                OnBazaar = new HashSet<string>(bazaar);
            }, "loading bazaar items");

        return filtered;
    }

    protected override IEnumerable<ProfitableCraft> FilterElementsForProfile(MinecraftSocket socket, IEnumerable<ProfitableCraft> elements)
    {
        var filtered = elements.Where(f => f.CraftCost < socket.SessionInfo.Purse && socket.SessionInfo.Purse > 0).ToList();
        if (filtered.Count != elements.Count())
            socket.Dialog(db => db.MsgLine($"Filtered {elements.Count() - filtered.Count} crafts that cost more than your purse ({socket.FormatPrice(socket.SessionInfo.Purse)})"));
        return filtered;
    }

    private static async Task<List<ProfitableCraft>> NewMethod(ICraftsApi craftApi)
    {
        var data = await craftApi.GetProfitableAsync();
        return data?.ToList() ?? new List<ProfitableCraft>();
    }

    protected override string GetId(ProfitableCraft elem)
    {
        return elem.ItemId + elem.Median;
    }
}
