using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Model;
using Newtonsoft.Json;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class ShareItemCommand : ItemSelectCommand<ShareItemCommand>
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        var targetPlayer = args[0];
        if (string.IsNullOrWhiteSpace(targetPlayer))
        {
            socket.Dialog(db => db.MsgLine("§cPlease specify a player to share the item with. Usage: /cofl shareitem <player>"));
            return;
        }
        var playerUuidTask = socket.GetPlayerUuid(targetPlayer);
        if (await playerUuidTask == null)
        {
            socket.Dialog(db => db.MsgLine($"§cPlayer {targetPlayer} not found, please check the spelling and try again"));
            return;
        }
        await HandleSelectionOrDisplaySelect(socket, args, targetPlayer, $"share this item with {targetPlayer}: \n");
    }

    protected override async Task SelectedItem(MinecraftSocket socket, string targetPlayer, Item item)
    {
        Activity.Current?.Log($"Sending item {item.ItemName} to {targetPlayer}\nJSON {JsonConvert.SerializeObject(item)}");
        socket.Dialog(db => db.MsgLine($"Sent {item.ItemName} to {targetPlayer}", null, item.Description).CoflCommand<ShareItemCommand>($"\"{targetPlayer}\"", targetPlayer, "send another item"));
        await socket.GetService<ChatService>().SendToChannel("dm-" + targetPlayer.ToLower(), new()
        {
            Prefix = "§7[§6§lDM§7]§r",
            Message = $"sent you an item to look at \n 物{JsonConvert.SerializeObject(item)}物",
            Name = socket.SessionInfo.McName,
            Uuid = socket.SessionInfo.McUuid
        });
    }
}
