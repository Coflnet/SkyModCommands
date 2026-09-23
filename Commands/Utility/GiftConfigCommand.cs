using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Gives a Config to another player for free (a Creator Gift)",
    "Coflnet does not deliver or update a Config to fulfil a sale made outside Coflnet",
    "Paid access is only available through the Coflnet marketplace, see /cofl buyconfig",
    "You can take a gift back with /cofl takeconfig")]
public class GiftConfigCommand : ArgumentsCommand
{
    internal const string ExternalSaleRefusalMessage =
        "giftconfig only creates free Creator Gifts. Coflnet does not deliver or update Configs to fulfil a sale made outside Coflnet. Paid access is only available through the Coflnet marketplace (/cofl buyconfig).";

    protected override string Usage => "<configName> <ign> [source={gift}]";

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!socket.SessionInfo.VerifiedMc)
        {
            socket.SendMessage("Verify this Minecraft account before gifting a Config.");
            return;
        }
        if (RequestsExternalSale(arguments))
        {
            socket.SendMessage(ExternalSaleRefusalMessage);
            return;
        }
        var creatorAgreement = await CurrentAgreement.GetCreator();
        var freePublisher = SellConfigCommand.IsFreePublisher(
            socket.SessionInfo.McUuid);
        var eligibility = freePublisher
            ? null
            : await socket.GetService<RewardLedgerClient>()
                .GetCreatorEligibility(socket.UserId,
                    socket.SessionInfo.McUuid, creatorAgreement.Hash);
        if (!freePublisher && eligibility?.Eligible != true)
        {
            socket.SendMessage("You are not currently admitted as a Config creator.");
            return;
        }
        if (!await CurrentAgreement.RequireCreator(socket))
            return;
        await base.Execute(socket, arguments);
    }

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var ign = args["ign"];
        var name = args["configName"];
        if (args["source"].Equals("external", StringComparison.OrdinalIgnoreCase))
        {
            socket.SendMessage(ExternalSaleRefusalMessage);
            return;
        }
        if (!args["source"].Equals("gift", StringComparison.OrdinalIgnoreCase))
        {
            socket.SendMessage("source must be gift.");
            return;
        }
        var from = socket.UserId;
        var key = SellConfigCommand.GetKeyFromname(name);
        using var config = await SelfUpdatingValue<ConfigContainer>.Create(
            from, key, () => null);
        if (config.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        if (config.Value.Delisted)
        {
            socket.SendMessage("This config is delisted and unavailable for new access grants.");
            return;
        }
        var targetUserId = await GetUserIdFromMcName(socket, ign);
        if (!await CurrentAgreement.HasMarketplaceAcceptance(targetUserId))
        {
            socket.SendMessage(
                $"{ign} must accept the current Expert Marketplace agreement through /cofl buyconfig before you can gift this Config.");
            return;
        }
        await using var ownedLock = await OwnedConfigLock.Acquire(
            socket.GetService<SettingsService>(), targetUserId);
        using var configs = await SelfUpdatingValue<OwnedConfigs>.Create(
            targetUserId, "owned_configs", () => new());
        if (configs.Value.Configs.Any(c => c.Name.Equals(
                name, StringComparison.OrdinalIgnoreCase) && c.OwnerId == from
            && c.RevokedAtUtc == null))
        {
            socket.SendMessage("The user already has this config.");
            return;
        }
        configs.Value.Configs.Add(BuildGift(
            name, config.Value, from, socket.SessionInfo.McName));
        await configs.Update();
        socket.Dialog(db => db.MsgLine(
            $"Gifted {name} to {ign}. Its first managed update period is {BuyConfigCommand.UpdateTermYears} years. Coflnet may extend it by {BuyConfigCommand.UpdateExtensionYears} years at no charge, but no extension is promised."));
    }

    /// <summary>
    /// A Creator Gift always grants free, revocable (via takeconfig) access;
    /// there is no external-sale variant.
    /// </summary>
    internal static OwnedConfigs.OwnedConfig BuildGift(
        string name, ConfigContainer config, string ownerId, string ownerName)
    {
        var now = DateTime.UtcNow;
        return new()
        {
            Name = name,
            Version = config.Version,
            ChangeNotes = config.ChangeNotes,
            OwnerId = ownerId,
            OwnerName = ownerName,
            BoughtAt = now,
            AccessUntilUtc = now.AddYears(BuyConfigCommand.UpdateTermYears),
            CreatorGift = true
        };
    }

    internal static bool RequestsExternalSale(string arguments) =>
        (arguments ?? "").Trim('"').Split(
            ' ', StringSplitOptions.RemoveEmptyEntries).Skip(2)
        .Any(value => value.Equals(
            "external", StringComparison.OrdinalIgnoreCase));
}
