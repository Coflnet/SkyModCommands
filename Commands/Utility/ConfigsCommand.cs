using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class ConfigsCommand : ListCommand<OwnedConfigs.OwnedConfig, List<OwnedConfigs.OwnedConfig>>
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
        var obj = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        return obj.Value.Configs;
    }

    protected override Task DefaultAction(MinecraftSocket socket, string args)
    {
        return base.List(socket, args);
    }

    protected override DialogBuilder FormatForList(DialogBuilder d, OwnedConfigs.OwnedConfig e)
    {
        return d.Msg($"§6{e.Name} §7v{e.Version} §6{e.PricePaid} CoflCoins", null, e.ChangeNotes)
            .CoflCommand<LoadConfigCommand>($"§aLoad", $"{e.OwnerId} {e.Name}");
    }

    protected override Task Update(MinecraftSocket socket, List<OwnedConfigs.OwnedConfig> newCol)
    {
        throw new CoflnetException("not_possible", "currently not possible");
    }
}
