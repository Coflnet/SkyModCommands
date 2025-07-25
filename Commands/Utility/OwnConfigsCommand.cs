using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra.Data.Linq;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists configs you purchased from /cofl configs",
    "This command allows you to see the configs you own",
    "You can load them with /cl loadconfig <ownerId> <name>",
    "or by clicking on the output of the command")]
public class OwnConfigsCommand : ListCommand<OwnedConfigs.OwnedConfig, List<OwnedConfigs.OwnedConfig>>
{
    protected override Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
    {
        throw new CoflnetException("not_possible", "use the /cl buyconfig command to buy configs");
    }

    protected override string Format(OwnedConfigs.OwnedConfig elem)
    {
        return elem.Name;
    }

    protected override string GetId(OwnedConfigs.OwnedConfig elem)
    {
        return elem.OwnerId + elem.Name;
    }

    protected override async Task<List<OwnedConfigs.OwnedConfig>> GetList(MinecraftSocket socket)
    {
        return await GetOwnConfigs(socket);
    }

    public static async Task<List<OwnedConfigs.OwnedConfig>> GetOwnConfigs(MinecraftSocket socket)
    {
        var obj = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        return obj.Value.Configs;
    }

    protected override DialogBuilder FormatForList(DialogBuilder d, OwnedConfigs.OwnedConfig e)
    {
        return d.Msg($"§6{e.Name} §7v{e.Version} §6{e.PricePaid} CoflCoins", null, e.ChangeNotes)
            .CoflCommand<LoadConfigCommand>($" §a[Load]", $"{e.OwnerId} {e.Name}", $"Load {e.Name}");
    }

    protected override async Task Remove(MinecraftSocket socket, string arguments)
    {
        var toRemove = (await Find(socket, arguments)).FirstOrDefault();
        if (toRemove == default)
        {
            socket.SendMessage("Config could not be removed.");
            return;
        }
        if (toRemove.PricePaid != 0)
        {
            socket.SendMessage("You can't remove a bought (non free) config.");
            return;
        }
        var obj = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs");
        obj.Value.Configs.RemoveAll(c => c.Name == toRemove.Name && c.OwnerId == toRemove.OwnerId);
        await obj.Update();
        socket.Dialog(db => db.MsgLine($"§6{toRemove.Name} §7v{toRemove.Version} §6removed"));
    }

    protected override Task Update(MinecraftSocket socket, List<OwnedConfigs.OwnedConfig> newCol)
    {
        throw new CoflnetException("not_possible", "currently not possible");
    }


}
