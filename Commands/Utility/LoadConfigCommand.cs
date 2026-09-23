using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class LoadConfigCommand : ArgumentsCommand
{
    protected override string Usage => "<ownerId> <configName>";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var owner = args["ownerId"];
        var ownerName = owner;
        var name = args["configName"];
        var ownedConfigs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        if (!int.TryParse(owner, out _))
            owner = await GetUserIdFromMcName(socket, owner);
        var inOwnerShip = GetOwnership(owner, name, ownedConfigs);
        if (inOwnerShip?.PurchaseTransactionId > 0
            && (ownedConfigs.Value.RevertedPurchaseIds.Contains(
                    inOwnerShip.PurchaseTransactionId)
                || BuyConfigCommand.IsReverted(
                    await BuyConfigCommand.GetTransactions(socket),
                    inOwnerShip.PurchaseTransactionId)))
        {
            var storage = socket.GetService<SettingsService>();
            await using var ownedLock = await OwnedConfigLock.Acquire(
                storage, socket.UserId);
            using var latest = await SelfUpdatingValue<OwnedConfigs>.Create(
                socket.UserId, "owned_configs", () => new());
            var revoked = latest.Value.Configs.Where(config =>
                config.PurchaseTransactionId
                    == inOwnerShip.PurchaseTransactionId).ToList();
            foreach (var config in revoked)
                config.RevokedAtUtc ??= System.DateTime.UtcNow;
            latest.Value.RevertedPurchaseIds.Add(
                inOwnerShip.PurchaseTransactionId);
            await latest.Update();
            if (!revoked.Any(revokedConfig => latest.Value.Configs.Any(active =>
                    active.RevokedAtUtc == null
                    && active.OwnerId == revokedConfig.OwnerId
                    && active.Name.Equals(revokedConfig.Name,
                        System.StringComparison.OrdinalIgnoreCase))))
                await ConfigsCommand.Unloadconfig(socket);
            socket.SendMessage(
                "This purchase was reverted, so the Expert Config is no longer available.");
            return;
        }
        var published = await GetConfig(owner, name);
        if (inOwnerShip == default)
        {
            socket.Dialog(db => db.CoflCommand<BuyConfigCommand>(
                published.Price == 0
                    ? $"Add this free config first. {McColorCodes.GOLD}[review and add]"
                    : $"You don't own this config. {McColorCodes.GOLD}[buy it]",
                $"{ownerName} {name}",
                "Review the Config licence and managed-update terms before using this config"));
            return;
        }
        var settingsService = socket.GetService<SettingsService>();
        var settings = BuyConfigCommand.HasManagedUpdates(inOwnerShip)
            ? published
            : await SellConfigCommand.GetArchived(
                settingsService, inOwnerShip.OwnerId,
                inOwnerShip.Name, inOwnerShip.Version)
                ?? throw new CoflnetException(
                    "config_archive_missing",
                    "Your supplied Config version is temporarily unavailable. Contact support.");
        if (settings?.Settings == null)
        {
            socket.Dialog(db => db.MsgLine("The config is invalid (completely empty), please contact the creator.")
                .MsgLine($"{McColorCodes.DARK_GRAY}Debug info: {owner} {name}"));
            return;
        }
        if (socket.sessionLifesycle.AccountSettings.Value == null)
        {
            throw new CoflnetException("missing_account_settings", "Account settings not loaded, please try reconnecting");
        }
        var configId = settings.Settings.BasedConfig;
        var hasBaseConfig = !string.IsNullOrWhiteSpace(configId)
            && configId.Contains(':');
        using var baseConfig = hasBaseConfig
            ? await GetContainer(socket, configId)
            : null;
        OwnedConfigs.OwnedConfig baseOwnership = null;
        if (hasBaseConfig && baseConfig?.Value == null)
        {
            socket.Dialog(db => db.MsgLine($"The configured base config doesn't exist, ask the creator to correct it."));
            return;
        }
        if (hasBaseConfig)
        {
            baseOwnership = GetOwnership(
                baseConfig.Value.OwnerId, baseConfig.Value.Name, ownedConfigs);
            if (baseOwnership == default)
            {
                socket.Dialog(db => db.MsgLine($"You aren't in procession of the base config ({baseConfig.Value.Name}) your config `{name}` is based on .")
                    .CoflCommand<BuyConfigCommand>($"[click to buy]", $"{baseConfig.Value.OwnerId} {baseConfig.Value.Name}", "Buy the base config to use this config\nLoad it afterwards"));
                return;
            }
        }

        if (BuyConfigCommand.HasManagedUpdates(inOwnerShip))
            await SellConfigCommand.EnsureArchived(settingsService, settings);
        var loadedBase = baseConfig?.Value;
        if (hasBaseConfig && !BuyConfigCommand.HasManagedUpdates(baseOwnership))
            loadedBase = await SellConfigCommand.GetArchived(
                settingsService, baseOwnership.OwnerId,
                baseOwnership.Name, baseOwnership.Version)
                ?? throw new CoflnetException(
                    "config_archive_missing",
                    "Your supplied base Config version is temporarily unavailable. Contact support.");
        else if (loadedBase != null)
            await SellConfigCommand.EnsureArchived(settingsService, loadedBase);
        var combinedSettings = BuildManagedSettings(
            settings, loadedBase, socket.UserId);
        FlipFilter.CopyRelevantToNew(
            combinedSettings, socket.sessionLifesycle.FlipSettings);

        inOwnerShip.ChangeNotes = settings.ChangeNotes;
        inOwnerShip.Version = settings.Version;
        if (baseOwnership != null)
            baseOwnership.Version = loadedBase.Version;
        await ownedConfigs.Update();
        await socket.sessionLifesycle.FlipSettings.Update(combinedSettings);
        var baseVersion = loadedBase?.Version ?? 0;
        await UpdateConfig(socket, inOwnerShip, baseVersion);
        socket.Dialog(db => db.MsgLine($"§6{settings.Name} §7v{settings.Version} §6loaded"));
        await socket.sessionLifesycle.FilterState.SubToConfigChanges();
        if (hasBaseConfig)
            socket.Dialog(db => db.MsgLine($"also §6{loadedBase.Name} §7v{loadedBase.Version} §6loaded (BaseConfig)"));

    }

    internal static FlipSettings BuildManagedSettings(
        ConfigContainer config,
        ConfigContainer baseConfig,
        string userId)
    {
        var managed = Clone(config.Settings);
        FlipFilter.CopyRelevantToNew(managed, new FlipSettings());
        managed.BlockExport = config.OwnerId != userId;
        if (baseConfig != null)
        {
            CopyIfFlagged(baseConfig.Settings.BlackList, managed.BlackList);
            CopyIfFlagged(baseConfig.Settings.WhiteList, managed.WhiteList);
        }
        return managed;

        static void CopyIfFlagged(
            List<ListEntry> oldList,
            List<ListEntry> newList)
        {
            var loadConfigLookup = newList.ToLookup(GetFilterKey);
            newList.RemoveAll(item => item.Tags?.Contains("from BaseConfig") == true);
            foreach (var filter in oldList.Where(filter =>
                !loadConfigLookup.Contains(GetFilterKey(filter))))
            {
                var copy = Clone(filter);
                copy.Tags ??= new List<string>();
                copy.Tags.Add("from BaseConfig");
                newList.Add(copy);
            }

            static string GetFilterKey(ListEntry e)
            {
                string[] ignore = [
                    StringName<MinProfitDetailedFlipFilter>(),
                    StringName<ProfitPercentageDetailedFlipFilter>(),
                    StringName<EstProfitPerHourDetailedFlipFilter>(),
                    StringName<ProfitDetailedFlipFilter>(),
                    StringName<MinProfitPercentageDetailedFlipFilter>()];
                var relevantFilters = e.filter?.Where(f => ignore.All(v => !f.Key.Equals(v, System.StringComparison.OrdinalIgnoreCase)));
                return e.ItemTag + string.Join(',', e.Tags ?? []) + string.Join(',', relevantFilters?.Select(f => $"{f.Key}={f.Value}") ?? []);
            }
            static string StringName<T>() where T : DetailedFlipFilter
            {
                return CamelCaseNameDictionary<DetailedFlipFilter>.GetCleardName<T>();
            }
        }
    }

    private static T Clone<T>(T value) =>
        JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));

    private static async Task UpdateConfig(
        IMinecraftSocket socket,
        OwnedConfigs.OwnedConfig inOwnerShip,
        int baseVersion)
    {
        socket.sessionLifesycle.AccountSettings.Value.LoadedConfig = inOwnerShip;
        socket.sessionLifesycle.AccountSettings.Value.BaseConfigVersion =
            baseVersion;
        await socket.sessionLifesycle.AccountSettings.Update();
    }

    private static async Task<ConfigContainer> GetConfig(string owner, string name)
    {
        var key = SellConfigCommand.GetKeyFromname(name);
        using var toLoad = await SelfUpdatingValue<ConfigContainer>.Create(owner, key, () => null);
        if (toLoad.Value == null)
        {
            throw new CoflnetException("not_found", "The config doesn't exist.");
        }
        var settings = toLoad.Value;
        return settings;
    }

    private static OwnedConfigs.OwnedConfig GetOwnership(string owner, string name, SelfUpdatingValue<OwnedConfigs> ownedConfigs)
    {
        return ownedConfigs.Value.Configs.FirstOrDefault(c =>
                c.Name.Equals(name, System.StringComparison.InvariantCultureIgnoreCase)
                && c.OwnerId == owner
                && c.RevokedAtUtc == null);
    }

    public static async Task<SelfUpdatingValue<ConfigContainer>> GetContainer(IMinecraftSocket socket, string configId)
    {
        var parts = configId.Split(':');
        if (parts.Length != 2)
        {
            socket.Dialog(db => db.MsgLine("The config base config is invalid, ask the creator to correct it."));
            return await SelfUpdatingValue<ConfigContainer>.CreateNoUpdate(() => null);
        }
        var userId = await GetUserIdFromMcName(socket, parts[0]);
        var baseKey = SellConfigCommand.GetKeyFromname(parts[1]);
        if (socket.SessionInfo.IsDebug)
        {
            socket.Dialog(db => db.MsgLine($"Debug: {userId} {baseKey} from {parts[1]}"));
        }
        return await SelfUpdatingValue<ConfigContainer>.Create(userId.ToString(), baseKey, () => null);
    }
}
