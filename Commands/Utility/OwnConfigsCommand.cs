using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra.Data.Linq;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;

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

    public static async Task<List<OwnedConfigs.OwnedConfig>> GetOwnConfigs(IMinecraftSocket socket)
    {
        var obj = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        return obj.Value.Configs.Where(config => config.RevokedAtUtc == null)
            .ToList();
    }

    protected override DialogBuilder FormatForList(DialogBuilder d, OwnedConfigs.OwnedConfig e)
    {
        var details = e.AccessUntilUtc == null
            ? e.ChangeNotes
            : $"{e.ChangeNotes}\nManaged updates {(BuyConfigCommand.HasManagedUpdates(e) ? "end" : "ended")} {e.AccessUntilUtc:u}; Coflnet may extend them by {BuyConfigCommand.UpdateExtensionYears} years free, but no extension is promised. Your licence to the supplied version remains."
                + (e.CreatorGift ? " (revocable creator gift)" : "");
        return d.Msg($"§6{e.Name} §7v{e.Version} §6{e.PricePaid} CoflCoins", null, details)
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
        await using var ownedLock = await OwnedConfigLock.Acquire(
            socket.GetService<SettingsService>(), socket.UserId);
        using var obj = await SelfUpdatingValue<OwnedConfigs>.Create(
            socket.UserId, "owned_configs", () => new());
        obj.Value.Configs.RemoveAll(c => c.PricePaid == 0
            && c.RevokedAtUtc == null
            && c.Name.Equals(toRemove.Name,
                System.StringComparison.OrdinalIgnoreCase)
            && c.OwnerId == toRemove.OwnerId);
        await obj.Update();
        socket.Dialog(db => db.MsgLine($"§6{toRemove.Name} §7v{toRemove.Version} §6removed"));
    }

    protected override Task Update(MinecraftSocket socket, List<OwnedConfigs.OwnedConfig> newCol)
    {
        throw new CoflnetException("not_possible", "currently not possible");
    }


}
