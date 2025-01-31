using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Displays craft flips you can do.",
    "Based on unlocked collectionsAnd slayer level")]
public class CraftsCommand : ReadOnlyListCommand<Crafts.Models.ProfitableCraft>
{
    protected override string Title => $"Most profitable Craft Flips you can do (includes collection and slayer level)";

    public override bool IsPublic => true;

    protected override string NoMatchText => "No profitable craft flips found currently :/";

    private HashSet<string> OnBazaar = new HashSet<string>();

    public CraftsCommand()
    {
        sorters.Add("profit", (a) => a.OrderByDescending(f => f.SellPrice - f.CraftCost));
        sorters.Add("cost", (a) => a.OrderByDescending(f => f.CraftCost));
        sorters.Add("volume", (a) => a.OrderByDescending(f => f.Volume));
        sorters.Add("percent", (a) => a.OrderByDescending(f => (f.SellPrice - f.CraftCost) / f.CraftCost));
        sorters.Add("median", (a) => a.OrderByDescending(f => f.Median - f.CraftCost);
        sorters.Add("bazaar", (a) => a.Where(a => OnBazaar.Contains(a.ItemId)).OrderByDescending(f => f.SellPrice - f.CraftCost));
    }
    protected override void Format(MinecraftSocket socket, DialogBuilder db, Crafts.Models.ProfitableCraft elem)
    {
        var ingedientList = string.Join('\n', elem.Ingredients.Select(i => $"{i.ItemId} {McColorCodes.AQUA}x{i.Count} {McColorCodes.GRAY}cost {McColorCodes.GOLD}{socket.FormatPrice(i.Cost)}"));
        var hoverText = $"{McColorCodes.GRAY}Ingredients for {elem.ItemName}:\n" + ingedientList
        + $"\n{McColorCodes.GRAY}CraftCost: {McColorCodes.GOLD}{socket.FormatPrice(elem.CraftCost)}"
        + $"\n{McColorCodes.GRAY}Volume: {McColorCodes.GREEN}{socket.FormatPrice(elem.Volume)}  {McColorCodes.YELLOW}Click to open recipe menu"
        + $"\n{McColorCodes.GRAY}Estimated Profit: {McColorCodes.AQUA}{socket.FormatPrice(elem.SellPrice - elem.CraftCost)}";
        var click = $"/recipe {elem.ItemId}";
        if (elem.ItemId == "???")
        {
            click = "/cofl buy starter_premium";
            hoverText = $"{McColorCodes.GRAY}You need starter premium or higher to see the top 3\n{McColorCodes.YELLOW}Click to buy a tier";
        }
        db.MsgLine($" {elem.ItemName} {McColorCodes.GRAY}for {McColorCodes.AQUA}{socket.FormatPrice(elem.Median)} {McColorCodes.YELLOW}[Open Recipe]", click, hoverText);
    }

    protected override DialogBuilder PrintResult(MinecraftSocket socket, string title, int page, IEnumerable<ProfitableCraft> toDisplay, int totalPages)
    {
        return DialogBuilder.New.MsgLine($"{title} (page {page}/{totalPages})")
                    .ForEach(toDisplay, (db, elem, i) =>
                    {
                        if (page <= 1 && i <= 2 && !socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.STARTER_PREMIUM))
                        {
                            elem.ItemName = "Top 3 require starter premium to see";
                            elem.ItemId = "???";
                        }
                        Format(socket, db, elem);
                    });
    }

    protected override async Task<IEnumerable<Crafts.Models.ProfitableCraft>> GetElements(MinecraftSocket socket, string val)
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

    private static async Task<List<Crafts.Models.ProfitableCraft>> NewMethod(ICraftsApi craftApi)
    {
        var data = await craftApi.CraftsProfitGetWithHttpInfoAsync();
        return JsonConvert.DeserializeObject<List<Crafts.Models.ProfitableCraft>>(data.RawContent);
    }

    protected override string GetId(Crafts.Models.ProfitableCraft elem)
    {
        return elem.ItemId + elem.Median;
    }
}
