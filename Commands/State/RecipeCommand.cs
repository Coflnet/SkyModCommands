using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Crafts.Client.Api;

namespace Coflnet.Sky.Commands.MC;

public class RecipeCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var itemId = arguments.Trim();
        if (string.IsNullOrEmpty(itemId))
        {
            socket.Dialog(db => db.MsgLine("Please provide an item ID or name to view the recipe."));
            return;
        }
        var searchResult = await socket.GetService<ISearchApi>().ApiItemSearchSearchValGetAsync(itemId);
        var name = searchResult?.FirstOrDefault()?.Name;
        itemId = searchResult?.FirstOrDefault()?.Id ?? itemId;

        var craftApi = socket.GetService<ICraftsApi>();
        var itemListTask = socket.GetService<Items.Client.Api.IItemsApi>().ItemsGetAsync();
        var recipe = await craftApi.GetRecipeAsync(itemId);
        if (recipe == null)
        {
            socket.Dialog(db => db.MsgLine($"No recipe found for item: {itemId}"));
            return;
        }
        string[][] location = [[recipe.A1, recipe.A2, recipe.A3],
                               [recipe.B1, recipe.B2, recipe.B3],
                               [recipe.C1, recipe.C2, recipe.C3]];
        var partCount = new Dictionary<string, int>();
        foreach (var row in location)
        {
            foreach (var item in row)
            {
                if (item == null || item == "null")
                {
                    continue;
                }
                var parts = item.Split(':');
                var count = int.Parse(parts[1]);
                partCount.TryGetValue(parts[0], out var existingCount);
                partCount[parts[0]] = existingCount + count;
            }
        }
        var itemLookup = (await itemListTask).ToDictionary(i => i.Tag, i => new { Name = i.Name, IsBazaar = i.Flags.Value.HasFlag(Api.Client.Model.ItemFlags.BAZAAR) });

        socket.Dialog(d => d.MsgLine($"Recipe:")
            .ForEach(location, (db, row) =>
            {
                db.ForEach(row, (db2, item) =>
                {
                    if (item == null || item == "null")
                    {
                        db2.Button("-", null, "Empty slot");
                        return;
                    }
                    var parts = item.Split(':');
                    var itemId = parts[0];
                    var info = itemLookup.GetValueOrDefault(itemId, new { Name = itemId, IsBazaar = false });
                    var command = info.IsBazaar ? $"/bazaar {info.Name}" : $"/ah {info.Name}";
                    db2.Button(info.Name.First().ToString(), command, "Open " + (info.IsBazaar ? "bazaar" : "auction house") + " for " + info.Name);
                }).LineBreak();
            }).LineBreak()
            .MsgLine("Parts needed:")
            .ForEach(partCount, (db, kvp) =>
            {
                var itemId = kvp.Key;
                var count = kvp.Value;
                var info = itemLookup.GetValueOrDefault(itemId, new { Name = itemId, IsBazaar = false });
                var command = info.IsBazaar ? $"/bazaar {info.Name}" : $"/ah {info.Name}";
                db.Button($"{info.Name} x{count}", command, $"Open {(info.IsBazaar ? "bazaar" : "auction house")} for {info.Name} (x{count})")
                    .LineBreak();
            })
            .MsgLine("Open recipe menu for " + name, $"/recipe {itemId}").LineBreak()
        );
    }
}
