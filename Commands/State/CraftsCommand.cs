using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
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
    protected override void Format(MinecraftSocket socket, DialogBuilder db, Crafts.Models.ProfitableCraft elem)
    {
        var ingedientList = string.Join('\n', elem.Ingredients.Select(i => $"{i.ItemId} {McColorCodes.AQUA}x{i.Count} {McColorCodes.GRAY}cost {McColorCodes.GOLD}{socket.FormatPrice(i.Cost)}"));
        var hoverText = "CraftingIngredients:\n" + ingedientList
        + $"\n{McColorCodes.GRAY}CraftCost: {McColorCodes.GOLD}{socket.FormatPrice(elem.CraftCost)}"
        + $"\n{McColorCodes.GRAY}Volume: {McColorCodes.GREEN}{socket.FormatPrice(elem.Volume)}";
        db.Msg($"{elem.ItemName} {McColorCodes.GRAY}for {McColorCodes.AQUA}{socket.FormatPrice(elem.Median)}", $"/recipe {elem.ItemId}", hoverText);
    }

    protected override async Task<IEnumerable<Crafts.Models.ProfitableCraft>> GetElements(MinecraftSocket socket, string val)
    {
        var craftApi = socket.GetService<ICraftsApi>();
        var profileApi = socket.GetService<IProfileClient>();
        var craftsTask = NewMethod(craftApi);
        var filtered = await profileApi.FilterProfitableCrafts(craftsTask, socket.SessionInfo.McUuid, "current");
        if (!socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.STARTER_PREMIUM))
        {
            foreach (var item in filtered.OrderByDescending(f => f.SellPrice - f.CraftCost).Take(5))
            {
                item.ItemName = "Top 3 require starter premium to see";
                item.ItemId = "???";
            }
        }
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
