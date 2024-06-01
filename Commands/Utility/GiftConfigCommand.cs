using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class GiftConfigCommand : ArgumentsCommand
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
        var toBebought = await SelfUpdatingValue<ConfigContainer>.Create(from, key, () => null);
        if (toBebought.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        var targetUserId = await GetUserIdFromMcName(socket, ign);
        var configs = await SelfUpdatingValue<OwnedConfigs>.Create(targetUserId, "owned_configs", () => new());
        if (configs.Value.Configs.Any(c => c.Name == name && c.OwnerId == targetUserId))
        {
            socket.SendMessage("The user already owns this config.");
            return;
        }
        configs.Value.Configs.Add(new OwnedConfigs.OwnedConfig()
        {
            Name = name,
            Version = toBebought.Value.Version,
            ChangeNotes = toBebought.Value.ChangeNotes,
            OwnerId = from,
            PricePaid = 0,
            BoughtAt = DateTime.UtcNow,
            OwnerName = socket.SessionInfo.McName
        });
        await configs.Update();
        socket.Dialog(db => db.MsgLine($"Gifted {name} to {ign}."));
    }
}