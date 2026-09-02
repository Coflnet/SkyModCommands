using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Takes back a Config you previously gifted with /cofl giftconfig",
    "Only removes Creator Gifts; purchased or otherwise added configs are never removed")]
public class TakeConfigCommand : ArgumentsCommand
{
    protected override string Usage => "<configName> <ign>";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var ign = args["ign"];
        var name = args["configName"];
        var from = socket.UserId;
        var key = SellConfigCommand.GetKeyFromname(name);
        // check it exists
        using var toBebought = await SelfUpdatingValue<ConfigContainer>.Create(from, key, () => null);
        if (toBebought.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        var targetUserId = await GetUserIdFromMcName(socket, ign);
        await using var ownedLock = await OwnedConfigLock.Acquire(
            socket.GetService<SettingsService>(), targetUserId);
        using var configs = await SelfUpdatingValue<OwnedConfigs>.Create(targetUserId, "owned_configs", () => new());
        var (matching, toRemove) = RevokeGiftedAccess(
            configs.Value, from, name, DateTime.UtcNow);
        foreach (var item in toRemove)
        {
            socket.Dialog(db => db.MsgLine($"Removed gifted access to {name} from {ign}."));
        }
        await configs.Update();
        if (!matching.Any(revokedConfig => configs.Value.Configs.Any(active =>
                active.RevokedAtUtc == null
                && active.OwnerId == revokedConfig.OwnerId
                && active.Name.Equals(revokedConfig.Name,
                    StringComparison.OrdinalIgnoreCase))))
            await ExpertConfigRefundService.ResetLoaded(
                socket.GetService<SettingsService>(), targetUserId, matching);
        if (toRemove.Count == 0)
        {
            socket.SendMessage("Removed no config as the user didn't have it (anymore) maybe it was already removed or never gifted");
        }
    }

    /// <summary>
    /// takeconfig only ever removes Creator Gifts (never a purchase or an
    /// otherwise added config). Returns every gift matching owner/name,
    /// including already-revoked ones, plus the subset newly revoked here.
    /// </summary>
    internal static (List<OwnedConfigs.OwnedConfig> Matching,
        List<OwnedConfigs.OwnedConfig> Revoked) RevokeGiftedAccess(
        OwnedConfigs configs, string from, string name, DateTime revokedAtUtc)
    {
        var matching = configs.Configs.Where(c => c.OwnerId == from
            && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && c.CreatorGift).ToList();
        var toRevoke = matching.Where(c => c.RevokedAtUtc == null).ToList();
        foreach (var item in toRevoke)
            item.RevokedAtUtc = revokedAtUtc;
        return (matching, toRevoke);
    }
}
