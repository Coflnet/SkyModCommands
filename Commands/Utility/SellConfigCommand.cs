using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.McConnect.Api;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Settings.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class SellConfigCommand : ArgumentsCommand
{
    private static HashSet<string> AllowedUsers = new HashSet<string>(){
            "e7246661de77474f94627fabf9880f60",
            "89481ffed0014b158805c67d4a21c757", // 788963904087916545
            "9f57ee301a82450da928f97cb2d1466c", // 470425365593063425
            "dcc434c06bf9463188a1c5ca09c3431d", // 476689961647865866
            "0c49ce5fdffc4783b99ff295de55908f", // 1232813401030525022
            "7b6e1ac1eb704e159702112aa21d1d97", // 781103483712569384
            "c248fe3bcbc740d795bb075b32acd70c", // 409404687796797450
            "1ea371eb83d04a8fb116aa3eb0047b23", // 1160548899711356958
        };

    protected override string Usage => "<name> [price=0] [changeLog (multi word)]";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        if ((!AllowedUsers.Contains(socket.SessionInfo.McUuid) && !socket.GetService<ModeratorService>().IsModerator(socket)) || !socket.SessionInfo.VerifiedMc)
        {
            socket.Dialog(db => db.Msg("You need to be whitelisted as config seller to use this command. Contact Äkwav on discord to agree to the terms.", null, "Please contact the server owner."));
            return;
        }
        var name = args["name"];
        if (name.Length == 0)
        {
            socket.SendMessage($"Usage: {McColorCodes.AQUA}/cl sellconfig <name> [price=0] [optional detail note what changed]{McColorCodes.GRAY}. The name is how it will be found via {McColorCodes.AQUA}/cl buyconfig {socket.SessionInfo.McName} <name>");
            return;
        }
        var text = args["changeLog"] ?? "";
        var price = args["price"];
        if (!int.TryParse(price, out var priceInt))
        {
            socket.SendMessage("The price has to be a number.");
            return;
        }
        if (priceInt % 600 != 0)
        {
            socket.SendMessage("The price has to be a multiple of 600.");
            return;
        }
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
        using var current = await SelfUpdatingValue<ConfigContainer>.Create(socket.UserId, key, () => config);
        if (current.Value.Version++ > 1)
        {
            current.Value.Settings = config.Settings;
            if (!string.IsNullOrEmpty(config.ChangeNotes))
                current.Value.ChangeNotes = config.ChangeNotes;
            if (config.Price != 0 || !string.IsNullOrEmpty(config.ChangeNotes))
                current.Value.Price = config.Price;
            await current.Update();
            socket.Dialog(db => db.MsgLine($"§6{config.Name} §7v{current.Value.Version} §6updated")
                .LineBreak()
                .MsgLine($"§7{current.Value.ChangeNotes}")
                .LineBreak()
                .MsgLine($"§7{current.Value.Price} CoflCoins"));
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
        var configsCommand = MinecraftSocket.Commands.GetBy<ConfigsCommand>();
        var table = configsCommand.GetTable(socket);
        var rating = await configsCommand.GetRatingOrDefault(table, name, new()
        {
            OwnerId = socket.UserId,
            Name = name,
            OwnerName = socket.SessionInfo.McName,
            PricePaid = priceInt
        });
        if (rating.OwnerName != socket.SessionInfo.McName)
        {
            rating.OwnerName = socket.SessionInfo.McName;
        }
        await table.Insert(rating).ExecuteAsync();
        // add to own configs
        using var createdConfigs = await SelfUpdatingValue<CreatedConfigs>.Create(socket.UserId, "created_configs", () => new());
        createdConfigs.Value.Configs.Add(name);
        await createdConfigs.Update();
        using var ownedConfigs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        if (ownedConfigs.Value.Configs.Any(c => c.Name == name && c.OwnerId == socket.UserId))
        {
            return;
        }
        ownedConfigs.Value.Configs.Add(new OwnedConfigs.OwnedConfig()
        {
            Name = name,
            Version = 1,
            ChangeNotes = text,
            OwnerId = socket.UserId,
            PricePaid = priceInt,
            OwnerName = socket.SessionInfo.McName
        });
        await ownedConfigs.Update();
        socket.Settings.BlockExport = false;
        await socket.sessionLifesycle.FlipSettings.Update();
    }

    public static string GetKeyFromname(string name)
    {
        return "seller_config_" + name.ToLower().Truncate(20);
    }
}