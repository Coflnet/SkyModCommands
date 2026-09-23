using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Updates the current config to the latest version",
    "This command applies all changes from the config seller",
    "This is done by checking what the seller changed and applying",
    " those changes. This keeps any filter changes you made in tact.",
    "You can skip settings by using /cl updateconfig skipSettings=true")]
public class UpdateCurrentConfigCommand : ArgumentsCommand
{
    protected override string Usage => "[skipSettings=true]";
    public override bool IsPublic => true;

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var skipSettings = args["skipSettings"] == "true";
        var loadedConfigMetadata = socket.sessionLifesycle.AccountSettings.Value.LoadedConfig;
        if (loadedConfigMetadata == null)
        {
            socket.SendMessage("No config loaded, aborting.");
            return;
        }
        var currentAccess = (await OwnConfigsCommand.GetOwnConfigs(socket)).FirstOrDefault(
            config => config.OwnerId == loadedConfigMetadata.OwnerId
                && config.Name.Equals(loadedConfigMetadata.Name,
                    StringComparison.OrdinalIgnoreCase));
        if (currentAccess == null)
        {
            await ConfigsCommand.Unloadconfig(socket);
            socket.SendMessage(
                "Your Config entitlement was removed, so it was unloaded.");
            return;
        }
        loadedConfigMetadata.AccessUntilUtc = currentAccess.AccessUntilUtc;
        if (!BuyConfigCommand.HasManagedUpdates(currentAccess))
        {
            socket.SendMessage(
                "Your managed update period ended; your licence to the supplied Config version remains.");
            return;
        }
        using var configData = await SelfUpdatingValue<ConfigContainer>.Create(
            loadedConfigMetadata.OwnerId,
            SellConfigCommand.GetKeyFromname(loadedConfigMetadata.Name),
            () => throw new CoflnetException("not_found", "config not found"));

        if (!configData.Value.Diffs.TryGetValue(loadedConfigMetadata.Version + 1, out _))
        {
            if (configData.Value.Version > loadedConfigMetadata.Version)
            {
                socket.SendMessage("Can't update config, unknown changes. If you want to update it reload it with /cl loadconfig");
                return;
            }
            socket.SendMessage("Config is up to date.");
            return;
        }
        var differ = new SettingsDiffer();
        var settings = socket.Settings;
        for (int i = loadedConfigMetadata.Version + 1; i <= configData.Value.Version; i++)
        {
            var diff = configData.Value.Diffs[i];
            settings = differ.ApplyDiff(settings, diff, skipSettings);
            var diffCount = diff.BlacklistAdded.Count + diff.BlacklistRemoved.Count + diff.WhitelistAdded.Count + diff.WhitelistRemoved.Count + diff.BlacklistChanged.Count + diff.WhitelistChanged.Count + diff.SetCommands.Count;
            Console.WriteLine(JsonConvert.SerializeObject(diff, Formatting.Indented));
            socket.Dialog(db => db.MsgLine($"Applied diff v{i} {McColorCodes.GRAY}with {diffCount} changes"));
        }
        settings.LastChanged = "config from seller";
        settings.UsedVersion = configData.Value.Version;
        await socket.sessionLifesycle.FlipSettings.Update(settings);
        loadedConfigMetadata.Version = configData.Value.Version;
        socket.sessionLifesycle.AccountSettings.Value.LoadedConfig = loadedConfigMetadata;
        await socket.sessionLifesycle.AccountSettings.Update();
    }
}
