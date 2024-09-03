using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class LoadConfigCommand : ArgumentsCommand
{
    protected override string Usage => "<ownerId> <configName>";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var owner = args["ownerId"];
        var name = args["configName"];
        var key = SellConfigCommand.GetKeyFromname(name);
        var ownedConfigs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        OwnedConfigs.OwnedConfig inOwnerShip = GetOwnership(owner, name, ownedConfigs);
        if (inOwnerShip == default)
        {
            socket.SendMessage("You don't own this config.");
            return;
        }
        if (!int.TryParse(owner, out _))
        {
            owner = inOwnerShip.OwnerId;
        }
        var toLoad = await SelfUpdatingValue<ConfigContainer>.Create(owner, key, () => null);
        if (toLoad.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        toLoad.Value.Settings.BlockExport = toLoad.Value.OwnerId != socket.UserId;

        FlipFilter.CopyRelevantToNew(toLoad.Value.Settings, socket.sessionLifesycle.FlipSettings);
        await socket.sessionLifesycle.FlipSettings.Update(toLoad.Value.Settings);
        socket.Dialog(db => db.MsgLine($"§6{toLoad.Value.Name} §7v{toLoad.Value.Version} §6loaded"));
        inOwnerShip.ChangeNotes = toLoad.Value.ChangeNotes;
        inOwnerShip.Version = toLoad.Value.Version;

        socket.sessionLifesycle.AccountSettings.Value.LoadedConfig = inOwnerShip;
        await socket.sessionLifesycle.AccountSettings.Update();
        await ownedConfigs.Update(); // update used version
        await socket.sessionLifesycle.FilterState.SubToConfigChanges();

        var configId = toLoad.Value.Settings.BasedConfig;
        if (configId == null)
            return;

        var baseConfig = await GetContainer(socket, configId);
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

        CopyIfFlagged(baseConfig.Value.Settings.BlackList, toLoad.Value.Settings.BlackList);
        CopyIfFlagged(baseConfig.Value.Settings.WhiteList, toLoad.Value.Settings.WhiteList);
        void CopyIfFlagged(List<ListEntry> oldList, List<ListEntry> newList)
        {
            foreach (var filter in oldList)
            {
                if (filter.Tags == null)
                {
                    filter.Tags = new List<string>();
                }
                filter.Tags.Add("from BaseConfig");
                newList.Add(filter);
            }
        }
        await socket.sessionLifesycle.FlipSettings.Update(toLoad.Value.Settings);
        socket.Dialog(db => db.MsgLine($"§6{baseConfig.Value.Name} §7v{baseConfig.Value.Version} §6loaded (BaseConfig)"));

        await socket.sessionLifesycle.FilterState.SubToConfigChanges();
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
