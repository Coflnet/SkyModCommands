using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class TakeConfigCommand : ArgumentsCommand
{
    protected override string Usage => "<configName> <ign>";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var ign = args["ign"];
        var name = args["configName"];
        var from = socket.UserId;
        if (name.Contains(':') && socket.SessionInfo.McName == "Ekwav" && socket.SessionInfo.VerifiedMc)
        {
            var parts = name.Split(':');
            name = parts[1];
            from = parts[0];
            socket.Dialog(db => db.MsgLine($"Overwrote sender {name} to {ign} from {from}."));
        }
        var key = SellConfigCommand.GetKeyFromname(name);
        // check it exists
        using var toBebought = await SelfUpdatingValue<ConfigContainer>.Create(from, key, () => null);
        if (toBebought.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        var targetUserId = await GetUserIdFromMcName(socket, ign);
        using var configs = await SelfUpdatingValue<OwnedConfigs>.Create(targetUserId, "owned_configs", () => new());
        var toRemove = configs.Value.Configs.Where(c => c.OwnerId == from && c.Name == name).ToList();
        foreach (var item in toRemove)
        {
            if(item.PricePaid != 0)
            {
                socket.Dialog(db => db.MsgLine($"Config was not gifted but bought by user, can't be take away."));
                continue;
            }
            configs.Value.Configs.Remove(item);
            socket.Dialog(db => db.MsgLine($"Removed {name} from {ign}."));
        }
        await configs.Update();
        if (toRemove.Count == 0)
        {
            socket.SendMessage("Removed no config as the user didn't have it (anymore) maybe it was already removed or never gifted");
        }
    }
}