using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class ShareItemCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        var targetPlayer = args[0];
        var playerUuidTask = socket.GetPlayerUuid(targetPlayer);
        var inventory = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdLastChestGetAsync(socket.SessionInfo.McName);
        if(await playerUuidTask == null)
        {
            socket.Dialog(db => db.MsgLine($"§cPlayer {targetPlayer} not found, please check the spelling and try again"));
            return;
        }
        if (args.Length > 1)
        {
            var index = int.Parse(args.Last());
            var item = inventory[index];
            if (item == null)
            {
                socket.SendMessage(new ChatPart("§cThere is no item in the selected slot"));
                return;
            }
            socket.Dialog(db => db.MsgLine($"Sent {item.ItemName} to {targetPlayer}").CoflCommand<ShareItemCommand>($"\"{targetPlayer}\"", targetPlayer, "send another item"));
            await socket.GetService<ChatService>().SendToChannel("dm-" + targetPlayer.ToLower(), new ()
            {
                Prefix = "§7[§6§lDM§7]§r",
                Message = $"sent you an item to look at \n 物{JsonConvert.SerializeObject(item)}物",
                Name = socket.SessionInfo.McName,
                Uuid = socket.SessionInfo.McUuid
            });
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