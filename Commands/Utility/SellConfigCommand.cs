using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.McConnect.Api;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Settings.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class SellConfigCommand : McCommand
{
    private static HashSet<string> AllowedUsers = new HashSet<string>(){
            "e7246661de77474f94627fabf9880f60"
        };
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if ((!AllowedUsers.Contains(socket.SessionInfo.McUuid) && !socket.GetService<ModeratorService>().IsModerator(socket)) || !socket.SessionInfo.VerifiedMc)
        {
            socket.SendMessage("You need to be whitelisted as config seller to use this command.", null, "Please contact the server owner.");
            return;
        }
        var name = arguments.Trim('"').Split(' ')[0];
        if (name.Length == 0)
        {
            socket.SendMessage($"Usage: {McColorCodes.AQUA}/cl sellconfig <name> <price> [optional detail note what changed]{McColorCodes.GRAY}. The name is how it will be found via {McColorCodes.AQUA}/cl buyconfig {socket.SessionInfo.McName} <name>");
            return;
        }
        var text = arguments.Trim('"').Substring(name.Length).Trim();
        var price = text.Split(' ')[0];
        if (!int.TryParse(price, out var priceInt))
        {
            socket.SendMessage("The price has to be a number.");
            return;
        }
        if(priceInt % 600 != 0)
        {
            socket.SendMessage("The price has to be a multiple of 600.");
            return;
        }
        text = text.Substring(price.Length).Trim();
        var settingsApi = socket.GetService<ISettingsApi>();
        string key = GetKeyFromname(name);
        var config = new ConfigContainer()
        {
            Name = name,
            Settings = JsonConvert.DeserializeObject<FlipSettings>(JsonConvert.SerializeObject(socket.Settings)),
            Version = 1,
            ChangeNotes = text,
            OwnerId = socket.UserId,
            Price = priceInt
        };
        var current = await SelfUpdatingValue<ConfigContainer>.Create(socket.UserId, key, () => config);
        if (current.Value.Version++ > 1)
        {
            current.Value.Settings = config.Settings;
            current.Value.ChangeNotes = config.ChangeNotes;
            current.Value.Price = config.Price;
            current.Value.ChangeNotes = config.ChangeNotes;
            await current.Update();
            socket.Dialog(db => db.MsgLine($"§6{config.Name} §7v{current.Value.Version} §6updated")
                .LineBreak()
                .MsgLine($"§7{config.ChangeNotes}")
                .LineBreak()
                .MsgLine($"§7{config.Price} CoflCoins"));
        }
        else
        {
            await current.Update();
            socket.Dialog(db => db.MsgLine($"§6{config.Name} §7v1 §6created")
                .LineBreak()
                .MsgLine($"§7{config.ChangeNotes}")
                .LineBreak()
                .MsgLine($"§7{config.Price} CoflCoins"));
        }
    }

    public static string GetKeyFromname(string name)
    {
        return "seller_config_" + name.ToLower().Truncate(20);
    }
}