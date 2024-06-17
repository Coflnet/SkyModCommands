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
        var inOwnerShip = ownedConfigs.Value.Configs.Where(c => c.Name.Equals(name, System.StringComparison.InvariantCultureIgnoreCase) && c.OwnerId == owner).FirstOrDefault()
            ?? ownedConfigs.Value.Configs.Where(c => c.Name.Equals(name, System.StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
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
        await socket.sessionLifesycle.FlipSettings.Update(toLoad.Value.Settings);
        socket.Dialog(db => db.MsgLine($"§6{toLoad.Value.Name} §7v{toLoad.Value.Version} §6loaded"));
        inOwnerShip.ChangeNotes = toLoad.Value.ChangeNotes;
        inOwnerShip.Version = toLoad.Value.Version;

        socket.sessionLifesycle.AccountSettings.Value.LoadedConfig = inOwnerShip;
        await socket.sessionLifesycle.AccountSettings.Update();
        await ownedConfigs.Update(); // update used version
        await socket.sessionLifesycle.SubToConfigChanges();
    }
}
