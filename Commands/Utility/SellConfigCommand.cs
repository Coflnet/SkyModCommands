using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.McConnect.Api;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.EntityFrameworkCore;
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
            "b1db5cbc4f7c4e51a372aa1bd19802c5", // 1113168701773062164
            "bf0d928d9d514ed5bf174215672e5c69", // 323511657051586561
            "2eef6b5f59b24bbe990b94ab3ec42fec", // 646394558095032330
            "5063065c0acb404c933d80027fe31634", // 1186628570265690206
            "9c880fc47d6c45279e2caa864989e6dc", // 1236888164854005780
            "6f71315b5dd04d6eb2770ea8d8e4db17", // 1227134439650496593
            "08375b58acea4ccf8daade140d8a300c", // 710159170787213343
            "c667b5f16c364ea7bc2ed76e000bc7ce", // 577668546037809194
            "a64f9bea93814e8e989657a3266ff733", // 868277476818825296
            "b67d2d7e18be4e70bfc93fbe0c3d8fc6", // 589153757346922497
            "72164cca9fa445a4943b01e3b0da58af", // 693703755505336450
            "7657f4b09dd24966aa76b6d203562082", // 153236171655348224
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
        if (priceInt % 600 != 0 && priceInt % 900 != 0)
        {
            socket.SendMessage("The price has to be a multiple of 600 or 900.");
            return;
        }
        if (int.TryParse(name, out _))
        {
            socket.SendMessage("Your config name is a number, this is probably an error and you meant to specify the price. Please correct the order of the arguments.");
            return;
        }
        string key = GetKeyFromname(name);
        var settingsCopy = JsonConvert.DeserializeObject<FlipSettings>(JsonConvert.SerializeObject(socket.Settings));
        RemoveBaseConfig(settingsCopy.WhiteList);
        RemoveBaseConfig(settingsCopy.BlackList);
        var config = new ConfigContainer()
        {
            Name = name,
            Settings = settingsCopy,
            Version = 1,
            ChangeNotes = text,
            OwnerId = socket.UserId,
            Price = priceInt,
            LastUpdated = DateTime.UtcNow
        };
        socket.Settings.PublishedAs = name;
        var configsCommand = MinecraftSocket.Commands.GetBy<ConfigsCommand>();
        var table = configsCommand.GetTable(socket);
        var all = await table.ToListAsync();
        if (all.Any(c => c.ConfigName == name && c.OwnerId != socket.UserId && c.OwnerName == socket.SessionInfo.McName))
        {
            socket.Dialog(db => db.Msg("This config name was already published by you with another email", null, "Please choose a different name."));
            return;
        }
        _ = socket.TryAsyncTimes(socket.sessionLifesycle.FlipSettings.Update, "update published as");
        using var current = await SelfUpdatingValue<ConfigContainer>.Create(socket.UserId, key, () => config);
        if (current.Value.Version++ > 1)
        {
            await UpdateConfig(socket, config, current);
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
        var rating = await configsCommand.GetRatingOrDefault(table, name, new()
        {
            OwnerId = socket.UserId,
            Name = name,
            OwnerName = socket.SessionInfo.McName,
            PricePaid = priceInt,
        });
        if (rating.OwnerName != socket.SessionInfo.McName)
        {
            rating.OwnerName = socket.SessionInfo.McName;
        }
        rating.LastUpdated = DateTime.UtcNow;
        await table.Insert(rating).ExecuteAsync();
        // add to own configs
        using var createdConfigs = await SelfUpdatingValue<CreatedConfigs>.Create(socket.UserId, "created_configs", () => new());
        createdConfigs.Value.Configs.Add(name);
        await createdConfigs.Update();
        using var ownedConfigs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        var owned = ownedConfigs.Value.Configs.FirstOrDefault(c => c.Name == name && c.OwnerId == socket.UserId);
        if (owned != null)
        {
            owned.Version = current.Value.Version;
            await ownedConfigs.Update();
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

    private static async Task UpdateConfig(IMinecraftSocket socket, ConfigContainer config, SelfUpdatingValue<ConfigContainer> current)
    {
        RemoveDupplicates(config.Settings.BlackList);
        RemoveDupplicates(config.Settings.WhiteList);
        var diff = SettingsDiffer.GetDifferences(current.Value.Settings, config.Settings);
        if (diff.GetDiffCount() == 0)
            throw new CoflnetException("no_changes", "No changes found in the config, aborting update");
        current.Value.Settings = config.Settings;
        current.Value.LastUpdated = DateTime.UtcNow;
        current.Value.Diffs.Add(current.Value.Version, diff);
        current.Value.Settings.UsedVersion = current.Value.Version;
        Console.WriteLine("found Diff: " + JsonConvert.SerializeObject(diff, Formatting.Indented));
        if (current.Value.Diffs.Count > 5)
        {
            current.Value.Diffs.Remove(current.Value.Diffs.Keys.Min());
        }
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

    private static void RemoveDupplicates(List<ListEntry> list)
    {
        var dupplicates = list.ToList()
                        .GroupBy(x => x.ItemTag + (x.filter == null ? "" : string.Join(',', x.filter.Select(f => f.ToString()))))
                        .Where(g => g.Count() > 1).SelectMany(g => g.Skip(1));
        foreach (var item in dupplicates)
        {
            list.Remove(item);
            Console.WriteLine("Removed dupplicate");
        }
    }

    private void RemoveBaseConfig(List<ListEntry> whiteList)
    {
        foreach (var item in whiteList.ToList())
        {
            if (item.Tags?.Contains("from BaseConfig") ?? false)
            {
                whiteList.Remove(item);
            }
        }
    }

    public static string GetKeyFromname(string name)
    {
        return "seller_config_" + name.ToLower().Truncate(20);
    }
}