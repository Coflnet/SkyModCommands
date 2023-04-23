using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.MC;

public class ShareItemCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"');
        var targetPlayer = args.Split(' ')[0];
        var inventory = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdLastChestGetAsync(socket.SessionInfo.McName);
        if (args.Length > 1)
        {
            var index = int.Parse(args.Split(' ').Last());
            var item = inventory[index];
            if (item == null)
            {
                socket.SendMessage(new ChatPart("§cThere is no item in the selected slot"));
                return;
            }
            socket.Dialog(db => db.MsgLine($"Sent {item.ItemName} to {targetPlayer}").CoflCommand<ShareItemCommand>($"\"{targetPlayer}\"", targetPlayer, "send another item"));
            
            return;
        }
        socket.Dialog(db => db.MsgLine("Select the item you want to share").ForEach(inventory.Batch(9),
                (db, itemRow, i) => db.ForEach(itemRow, (db, item, j)
                    => db.CoflCommand<ShareItemCommand>(
                        GetInventoryRepresent(socket, item),
                        $"{targetPlayer} {i * 9 + j}",
                        $"share this item with {targetPlayer}: \n{item.ItemName}\n{item.Description}")).LineBreak()));
    }

    private static string GetInventoryRepresent(MinecraftSocket socket, Item item)
    {
        if (string.IsNullOrWhiteSpace(item.ItemName))
            return "§7[   ]";
        var rarityColor = item.ItemName.StartsWith("§") ? item.ItemName.Substring(0, 2) : "§7";
        var name = Regex.Replace(item.ItemName, @"§[\da-f]", "");
        System.Console.WriteLine(name);
        return $"{rarityColor}[{name.Truncate(2)}]";
    }
}