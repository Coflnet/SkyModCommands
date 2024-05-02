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
        if (!ownedConfigs.Value.Configs.Any(c => c.Name == name && c.OwnerId == owner))
        {
            socket.SendMessage("You don't own this config.");
            return;
        }
        var toLoad = await SelfUpdatingValue<ConfigContainer>.Create(owner, key, () => null);
        if (toLoad.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        await socket.sessionLifesycle.FlipSettings.Update(toLoad.Value.Settings);
        socket.Dialog(db => db.MsgLine($"§6{toLoad.Value.Name} §7v{toLoad.Value.Version} §6loaded"));
    }
}
