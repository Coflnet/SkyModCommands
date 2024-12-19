using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.Commands.MC;

public class ShareLoreCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        var targetPlayer = args[0];
        if (string.IsNullOrWhiteSpace(targetPlayer))
        {
            socket.Dialog(db => db.MsgLine("§cPlease specify a player to share the item with. Usage: /cofl sharelore <player>"));
            return;
        }
        var playerUuidTask = socket.GetPlayerUuid(targetPlayer);
        if (await playerUuidTask == null)
        {
            socket.Dialog(db => db.MsgLine($"§cPlayer {targetPlayer} not found, please check the spelling and try again"));
            return;
        }
        await SendTo(socket, targetPlayer);
    }

    protected async Task SendTo(MinecraftSocket socket, string targetPlayer)
    {
        var service = socket.GetService<SettingsService>();
        var loreSettings = await service.GetCurrentValue<DescriptionSetting>(socket.UserId, "description", () =>
                {
                    return DescriptionSetting.Default;
                });
        await socket.GetService<ChatService>().SendToChannel("dm-" + targetPlayer.ToLower(), new()
        {
            Prefix = "§7[§6§lDM§7]§r",
            Message = $"sent you lore settings \n 傳{JsonConvert.SerializeObject(loreSettings)}傳",
            Name = socket.SessionInfo.McName,
            Uuid = socket.SessionInfo.McUuid
        });
        socket.Dialog(db => db.MsgLine($"Sent lore settings to {targetPlayer}", null));
    }
}
