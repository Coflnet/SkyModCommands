using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class SellInventoryCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (socket.ModAdapter is not AfVersionAdapter adapter || socket.Version.Contains("afclient"))
            throw new CoflnetException("forbidden", "This command is only available with an autoflipper client like BAF");
        socket.SessionInfo.SellAll = true;
        if (socket.SessionInfo.Inventory == null)
        {
            socket.Send(Response.Create("getInventory", new
            {
                Location = "main"
            }));
            await Task.Delay(1500);
            if (socket.SessionInfo.Inventory == null)
                throw new CoflnetException("missing_inventory", "Your client did not upload an inventory yet, please try again in a few seconds");
        }
        foreach (var item in socket.SessionInfo.Inventory.Skip(9).Where(i => i != null && !i.ItemName.Contains("Menu")))
        {
            Activity.Current.Log("selling " + item.ItemName);
            var suffix = "";
            if (item.Count > 1)
                suffix = $" x{item.Count}";
            socket.Dialog(db => db.MsgLine($"§7[§6§lSelling§7]§r{item.ItemName}{suffix}"));
        }
        socket.Dialog(db => db.Msg("Starting to sell all items in your inventory (except your armor). \nPlease make sure there is nothing in your inventory you don't want to sell (see above for list)."));
        await adapter.TryToListAuction();
        var maxItems = socket.Settings?.ModSettings?.MaxFlipItemsInInventory ?? 0;
        if(maxItems == 0)
        {
            socket.Dialog(db => db.Msg($"You can avoid getting more flips/items by using {McColorCodes.AQUA}/cofl set maxItemsInInventory 1"));
        }
    }
}
