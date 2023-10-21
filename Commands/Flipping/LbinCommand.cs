using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class LbinCommand : McCommand
{
    FilterParser parser = new FilterParser();
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = JsonConvert.DeserializeObject<string>(arguments);
        if (args.Length == 0)
        {
            socket.Dialog(db =>
                db.MsgLine($"{McColorCodes.GREEN}Usage: /cofl lbin <item name> [filter=value] [filter=value-numeric]",
                null,
                "§7Example: /cofl lbin diamond sword rarity=epic\n"
                + "The itemsearch is the same as on the website\n"
                + "List of filters is available with /cl filters [search]\n"));
            return;
        }
        var filters = new Dictionary<string, string>();
        var itemName = await parser.ParseFiltersAsync(socket, args, filters, FlipFilter.AllFilters);
        var items = await socket.GetService<Items.Client.Api.IItemsApi>().ItemsSearchTermGetAsync(itemName);
        var itemId = ItemDetails.Instance.GetItemIdForTag(items.First().Tag);
        socket.SendMessage($"Querying AH for {itemName} ...");
        Activity.Current.Log($"Item id: {itemId} for {itemName}");
        var fe = socket.GetService<FilterEngine>();
        using var context = new HypixelContext();
        var auctions = await fe.AddFilters(context.Auctions
                    .Where(a => a.ItemId == itemId && a.End > DateTime.Now && a.HighestBidAmount == 0 && a.Bin), filters)
                    .OrderBy(a => a.StartingBid).Take(5).ToListAsync();
        socket.Dialog(db =>
            db.Break.ForEach(auctions.OrderByDescending(a => a.StartingBid), (d, a) => d.MsgLine($" §e{a.StartingBid}§7: {a.End}", $"/viewauction {a.Uuid}", a.ItemName))
            .MsgLine($"{McColorCodes.GREEN}Trying to open the lowest bin ..."));
        var lowest = auctions.OrderBy(a => a.StartingBid).FirstOrDefault();
        if (lowest == null)
        {
            socket.SendMessage("Sorry there was no auction found");
            return;
        }
        socket.ExecuteCommand($"/viewauction {lowest.Uuid}");
    }
}