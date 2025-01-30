using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

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
        OwnedConfigs.OwnedConfig inOwnerShip = GetOwnership(owner, name, ownedConfigs);
        if (!int.TryParse(owner, out _))
        {
            if (inOwnerShip == default)
            {
                owner = await GetUserIdFromMcName(socket, owner);
            }
            else
                owner = inOwnerShip.OwnerId;
        }
        ConfigContainer settings = await GetConfig(owner, name);
        if (inOwnerShip == default)
        {
            if (settings.Price == 0)
            {
                inOwnerShip = MakeConfigOwned(ownerName, ownedConfigs, settings);
            }
            else
            {
                socket.Dialog(db => db.CoflCommand<BuyConfigCommand>($"You don't own this config. {McColorCodes.GOLD}[buy it]", $"{owner} {name}", "Buy the config to use it"));
                return;
            }
        }
        if (settings?.Settings == null)
        {
            socket.Dialog(db => db.MsgLine("The config is invalid (completely empty), please contact the creator.")
                .MsgLine($"{McColorCodes.DARK_GRAY}Debug info: {owner} {name}"));
            return;
        }
        settings.Settings.BlockExport = settings.OwnerId != socket.UserId;

        FlipFilter.CopyRelevantToNew(settings.Settings, socket.sessionLifesycle.FlipSettings);
        socket.Dialog(db => db.MsgLine($"§6{settings.Name} §7v{settings.Version} §6loaded"));
        inOwnerShip.ChangeNotes = settings.ChangeNotes;
        inOwnerShip.Version = settings.Version;
        if (socket.sessionLifesycle.AccountSettings.Value == null)
        {
            throw new CoflnetException("missing_account_settings", "Account settings not loaded, please try reconnecting");
        }

        await ownedConfigs.Update(); // update used version
        await socket.sessionLifesycle.FilterState.SubToConfigChanges();
        _ = socket.TryAsyncTimes(async () =>
        {
            await Task.Delay(12000);
            await UpdateConfig(socket, inOwnerShip);
        }, "updateConfig", 1);
        await UpdateConfig(socket, inOwnerShip);

        var configId = settings.Settings.BasedConfig;
        if (string.IsNullOrWhiteSpace(configId))
        {
            await socket.sessionLifesycle.FlipSettings.Update(settings.Settings);
            return;
        }

        using var baseConfig = await GetContainer(socket, configId);
        if (baseConfig.Value == null)
        {
            socket.Dialog(db => db.MsgLine($"The configured base config doesn't exist, ask the creator to correct it."));
            return;
        }
        var baseOwnership = GetOwnership(baseConfig.Value.OwnerId, baseConfig.Value.Name, ownedConfigs);
        if (baseOwnership == default)
        {
            socket.Dialog(db => db.MsgLine($"You aren't in procession of the base config ({baseConfig.Value.Name}) your config `{name}` is based on .")
                .CoflCommand<BuyConfigCommand>($"[click to buy]", $"{baseConfig.Value.OwnerId} {baseConfig.Value.Name}", "Buy the base config to use this config\nLoad it afterwards"));
            return;
        }
        CopyIfFlagged(baseConfig.Value.Settings.BlackList, settings.Settings.BlackList);
        CopyIfFlagged(baseConfig.Value.Settings.WhiteList, settings.Settings.WhiteList);
        void CopyIfFlagged(List<ListEntry> oldList, List<ListEntry> newList)
        {
            var loadConfigLookup = newList.ToLookup(e => GetFilterKey(e));
            foreach (var item in newList.ToList())
            {
                if (item.Tags == null || !item.Tags.Contains("from BaseConfig"))
                {
                    continue;
                }
                newList.Remove(item);
            }
            foreach (var filter in oldList)
            {
                if (loadConfigLookup.Contains(GetFilterKey(filter)))
                {
                    continue;
                }
                if (filter.Tags == null)
                {
                    filter.Tags = new List<string>();
                }
                filter.Tags.Add("from BaseConfig");
                newList.Add(filter);
            }

            static string GetFilterKey(ListEntry e)
            {
                string[] ignore = [
                    StringName<MinProfitDetailedFlipFilter>(),
                    StringName<ProfitPercentageDetailedFlipFilter>(),
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
        await socket.sessionLifesycle.FlipSettings.Update(settings.Settings);
        socket.Dialog(db => db.MsgLine($"also §6{baseConfig.Value.Name} §7v{baseConfig.Value.Version} §6loaded (BaseConfig)"));
        socket.sessionLifesycle.AccountSettings.Value.BaseConfigVersion = baseConfig.Value.Version;

        await socket.sessionLifesycle.FilterState.SubToConfigChanges();
        await UpdateConfig(socket, inOwnerShip);
    }


    private static async Task UpdateConfig(IMinecraftSocket socket, OwnedConfigs.OwnedConfig inOwnerShip)
    {
        socket.sessionLifesycle.AccountSettings.Value.LoadedConfig = inOwnerShip;
        await socket.sessionLifesycle.AccountSettings.Update();
    }

    private static OwnedConfigs.OwnedConfig MakeConfigOwned(string ownerName, SelfUpdatingValue<OwnedConfigs> ownedConfigs, ConfigContainer settings)
    {
        // implicitly buy the config
        OwnedConfigs.OwnedConfig inOwnerShip = new OwnedConfigs.OwnedConfig
        {
            Name = settings.Name,
            OwnerId = settings.OwnerId,
            Version = settings.Version,
            ChangeNotes = settings.ChangeNotes,
            BoughtAt = System.DateTime.UtcNow,
            OwnerName = ownerName,
            PricePaid = 0
        };
        ownedConfigs.Value.Configs.Add(inOwnerShip);
        return inOwnerShip;
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
        return ownedConfigs.Value.Configs.Where(c => c.Name.Equals(name, System.StringComparison.InvariantCultureIgnoreCase) && c.OwnerId == owner).FirstOrDefault()
            ?? ownedConfigs.Value.Configs.Where(c => c.Name.Equals(name, System.StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
    }

    public static async Task<SelfUpdatingValue<ConfigContainer>> GetContainer(IMinecraftSocket socket, string configId)
    {
        var parts = configId.Split(':');
        if (parts.Length != 2)
        {
            socket.Dialog(db => db.MsgLine("The config base config is invalid, ask the creator to correct it."));
            return null;
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
