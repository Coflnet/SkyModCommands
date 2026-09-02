using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class SellConfigCommand : ArgumentsCommand
{
    private static readonly HashSet<string> FreePublishers = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "e7246661de77474f94627fabf9880f60",
        "89481ffed0014b158805c67d4a21c757",
        "9f57ee301a82450da928f97cb2d1466c",
        "dcc434c06bf9463188a1c5ca09c3431d",
        "0c49ce5fdffc4783b99ff295de55908f",
        "7b6e1ac1eb704e159702112aa21d1d97",
        "c248fe3bcbc740d795bb075b32acd70c",
        "1ea371eb83d04a8fb116aa3eb0047b23",
        "b1db5cbc4f7c4e51a372aa1bd19802c5",
        "bf0d928d9d514ed5bf174215672e5c69",
        "2eef6b5f59b24bbe990b94ab3ec42fec",
        "5063065c0acb404c933d80027fe31634",
        "9c880fc47d6c45279e2caa864989e6dc",
        "6f71315b5dd04d6eb2770ea8d8e4db17",
        "08375b58acea4ccf8daade140d8a300c",
        "c667b5f16c364ea7bc2ed76e000bc7ce",
        "a64f9bea93814e8e989657a3266ff733",
        "b67d2d7e18be4e70bfc93fbe0c3d8fc6",
        "72164cca9fa445a4943b01e3b0da58af",
        "7657f4b09dd24966aa76b6d203562082",
        "a5cd11497f3b434186df65f95249e03e",
        "b729c9f58e894097acd08e41906c6c5d",
        "cebad0b9c1ad444e9bf1d9144b687305",
        "dd2458f388ae401481c30d0d1fadd283"
    };

    protected override string Usage => "<name> [price=0] [changeLog (multi word)]";

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!socket.SessionInfo.VerifiedMc)
        {
            socket.SendMessage("Verify this Minecraft account before seller onboarding.");
            return;
        }
        var creatorAgreement = await CurrentAgreement.GetCreator();
        if (!IsFreePublisher(socket.SessionInfo.McUuid)
            && !(await socket.GetService<RewardLedgerClient>().GetCreatorEligibility(
                socket.UserId,
                socket.SessionInfo.McUuid,
                creatorAgreement.Hash)).Eligible)
        {
            socket.Dialog(db => db.Msg(
                "You need to be manually whitelisted as a config seller before accepting the Creator agreement or publishing configs. Contact Äkwav on Discord.",
                null,
                "Please contact the mod maintainer."));
            return;
        }

        var parts = (arguments ?? "").Trim('"').Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.FirstOrDefault() == "accept")
        {
            if (parts.Length != 3)
            {
                socket.SendMessage("Use the acceptance button shown by /cofl sellconfig.");
                return;
            }
            await CurrentAgreement.AcceptCreator(socket, parts[1], parts[2]);
            socket.Dialog(db => db.MsgLine(
                "The Creator Marketplace agreement was accepted. Run sellconfig again to publish."));
            return;
        }

        if (!await CurrentAgreement.RequireCreator(socket))
            return;
        await base.Execute(socket, arguments);
    }

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        if (socket.Settings.BlockExport)
            throw new CoflnetException("protected_config",
                "Unload the Expert Config before publishing your own settings.");
        var name = args["name"];
        if (name.Length == 0)
        {
            socket.SendMessage($"Usage: {McColorCodes.AQUA}/cl sellconfig <name> [price=0] [optional detail note what changed]{McColorCodes.GRAY}. The name is how it will be found via {McColorCodes.AQUA}/cl buyconfig {socket.SessionInfo.McName} <name>");
            return;
        }
        if (name.Length > 20)
        {
            socket.SendMessage("The config name must be at most 20 characters.");
            return;
        }
        var text = args["changeLog"] ?? "";
        var price = args["price"];
        if (!int.TryParse(price, out var priceInt))
        {
            socket.SendMessage("The price has to be a number.");
            return;
        }
        if (priceInt < 0)
        {
            socket.SendMessage("The price can't be negative.");
            return;
        }
        if (priceInt > 21600)
        {
            socket.SendMessage("Since the price can not be more than 21600 it was limited to that.");
            priceInt = 21600;
        }
        if (priceInt > 0)
        {
            var creatorAgreement = await CurrentAgreement.GetCreator();
            var paidEligibility = await socket.GetService<RewardLedgerClient>()
                .GetCreatorEligibility(
                    socket.UserId,
                    socket.SessionInfo.McUuid,
                    creatorAgreement.Hash);
            if (!paidEligibility.PaidPublicationReady)
            {
                socket.SendMessage(
                    "Your review currently permits free Configs only. Paid publication requires a supported seller territory and tax document route.");
                return;
            }
            await socket.GetService<RewardLedgerClient>().EnsureReady();
            var unitPrice = await BuyConfigCommand.GetPurchaseUnitPrice(socket);
            if (priceInt % unitPrice != 0)
            {
                socket.SendMessage(
                    $"The price must be divisible by the current {unitPrice}-CoflCoin checkout unit.");
                return;
            }
            socket.SendMessage(socket.GetService<RewardLedgerClient>()
                .DescribeValuation(priceInt));
        }
        if (int.TryParse(name, out _))
        {
            socket.SendMessage("Your config name is a number, this is probably an error and you meant to specify the price. Please correct the order of the arguments.");
            return;
        }
        string key = GetKeyFromname(name);
        using var current = await SelfUpdatingValue<ConfigContainer>.Create(
            socket.UserId, key, () => null);
        if (current.Value?.ModeratorDelisted == true)
        {
            socket.SendMessage(
                "This config was removed by a moderator and cannot be republished without review.");
            return;
        }
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
            OwnerMinecraftUuid = socket.SessionInfo.McUuid,
            Price = priceInt,
            LastUpdated = DateTime.UtcNow
        };
        socket.Settings.PublishedAs = name;
        var configsCommand = MinecraftSocket.Commands.GetBy<ConfigsCommand>();
        var table = configsCommand.GetTable();
        var all = (await table.ExecuteAsync()).ToList();
        if (all.Any(c => c.ConfigName.Equals(name, StringComparison.OrdinalIgnoreCase) && c.OwnerId != socket.UserId))
        {
            var existingConfig = all.First(c => c.ConfigName.Equals(name, StringComparison.OrdinalIgnoreCase) && c.OwnerId != socket.UserId);
            socket.Dialog(db => db.Msg($"This config name is already published by {McColorCodes.GOLD}{existingConfig.OwnerName}{McColorCodes.GRAY}. Please choose a different name.", null, "Config name is already taken."));
            return;
        }
        _ = socket.TryAsyncTimes(socket.sessionLifesycle.FlipSettings.Update, "update published as");
        if (current.Value != null)
        {
            current.Value.Version++;
            await UpdateConfig(socket, config, current);
        }
        else
        {
            await current.Update(config);
            socket.Dialog(db => db.MsgLine($"§6{config.Name} §7v1 §6created")
                .LineBreak()
                .MsgLine($"§7{config.ChangeNotes}")
                .LineBreak()
                .MsgLine($"§7{config.Price} CoflCoins"));
        }
        await EnsureArchived(
            socket.GetService<SettingsService>(), current.Value ?? config);
        await UpdateConfigRating(socket.UserId, name, priceInt, socket.SessionInfo.McName);
        // add to own configs
        using var createdConfigs = await SelfUpdatingValue<CreatedConfigs>.Create(socket.UserId, "created_configs", () => new());
        createdConfigs.Value.Configs.Add(name);
        // remove different casing versions
        createdConfigs.Value.Configs.RemoveWhere(c => c.Equals(name, StringComparison.OrdinalIgnoreCase) && c != name);
        await createdConfigs.Update();
        using var ownedConfigs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        var owned = ownedConfigs.Value.Configs.FirstOrDefault(c => c.Name.Equals(
                name, StringComparison.OrdinalIgnoreCase)
            && c.OwnerId == socket.UserId && c.RevokedAtUtc == null);
        if (owned != null)
        {
            owned.Version = current.Value?.Version ?? config.Version;
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

    public static async Task UpdateConfigRating(string userId, string name, int priceInt, string mcName = null)
    {
        var configsCommand = MinecraftSocket.Commands.GetBy<ConfigsCommand>();
        var table = configsCommand.GetTable();
        var rating = await configsCommand.GetRatingOrDefault(table, name, new()
        {
            OwnerId = userId,
            Name = name,
            OwnerName = mcName,
            PricePaid = priceInt,
        });
        if (rating.OwnerName != mcName && mcName != null)
        {
            rating.OwnerName = mcName;
        }
        rating.LastUpdated = DateTime.UtcNow;
        await table.Insert(rating).ExecuteAsync();
    }

    internal static async Task EnsureArchived(
        SettingsService settings,
        ConfigContainer config)
    {
        if (await GetArchived(settings, config.OwnerId, config.Name, config.Version)
            != null)
            return;
        await settings.UpdateSetting(
            config.OwnerId + "_archive",
            GetKeyFromname(config.Name) + $"_version_{config.Version}",
            config);
    }

    internal static Task<ConfigContainer> GetArchived(
        SettingsService settings,
        string ownerId,
        string name,
        int version) => settings.GetCurrentValue<ConfigContainer>(
            ownerId + "_archive",
            GetKeyFromname(name) + $"_version_{version}",
            () => null);

    private static async Task UpdateConfig(IMinecraftSocket socket, ConfigContainer config, SelfUpdatingValue<ConfigContainer> current)
    {
        RemoveDupplicates(config.Settings.BlackList);
        RemoveDupplicates(config.Settings.WhiteList);
        var diff = SettingsDiffer.GetDifferences(current.Value.Settings, config.Settings);
        var metadataChanged = current.Value.Delisted
            || current.Value.Price != config.Price
            || current.Value.OwnerMinecraftUuid != config.OwnerMinecraftUuid
            || !string.IsNullOrEmpty(config.ChangeNotes)
                && current.Value.ChangeNotes != config.ChangeNotes;
        if (diff.GetDiffCount() == 0 && !metadataChanged)
            throw new CoflnetException("no_changes", "No changes found in the config, aborting update");
        current.Value.Settings = config.Settings;
        current.Value.OwnerMinecraftUuid = config.OwnerMinecraftUuid;
        current.Value.Delisted = false;
        current.Value.LastUpdated = DateTime.UtcNow;
        current.Value.Diffs.Add(current.Value.Version, diff);
        current.Value.Price = config.Price;
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

    internal static bool IsFreePublisher(string minecraftUuid) =>
        minecraftUuid != null
        && FreePublishers.Contains(minecraftUuid.Replace("-", ""));

}
