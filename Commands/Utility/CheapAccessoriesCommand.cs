using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists the cheapest missing accessories per magical power",
    "Reads your accessory bag from the profile api and",
    "from accessory bag pages you opened with the mod.",
    "Upgrades only count the power gained over what you own")]
public class CheapAccessoriesCommand : ReadOnlyListCommand<CheapAccessory>
{
    public override bool IsPublic => true;
    protected override string Title => "Cheapest accessories per magical power";

    protected override void Format(MinecraftSocket socket, DialogBuilder db, CheapAccessory item)
    {
        db.MsgLine($" {item.ItemName} for {McColorCodes.AQUA}{socket.FormatPrice(item.PricePerPower)} coins {McColorCodes.GRAY}per magical power",
            "/ahs " + item.ItemName,
            $"{McColorCodes.YELLOW}Click to search the auction house\n" +
            $"{McColorCodes.GRAY}Lowest bin {socket.FormatPrice(item.Price)} for +{item.PowerGain} magical power");
    }

    protected override async Task<IEnumerable<CheapAccessory>> GetElements(MinecraftSocket socket, string val)
    {
        var service = socket.GetService<IAccessoryService>();
        var owned = await service.GetOwnedAccessories(socket.SessionInfo.McUuid, socket.SessionInfo.ProfileId);
        SendOwnedSummary(socket, owned);
        var amount = socket.SessionInfo.SessionTier switch
        {
            AccountTier.PREMIUM_PLUS => 1000,
            AccountTier.PREMIUM => 500,
            _ => 100
        };
        return await service.GetCheapestPerMagicalPower(owned, amount);
    }

    private static void SendOwnedSummary(MinecraftSocket socket, OwnedAccessories owned)
    {
        if (owned.Tags.Count == 0)
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}No accessories found, do you have inventory api on? {McColorCodes.GRAY}You can also open your accessory bag"));
        else
            socket.Dialog(db => db.MsgLine($"Skipping {owned.Tags.Count} accessories you already have"));
        if (owned.BagPartiallySeen)
            socket.Dialog(db => db.MsgLine($"{McColorCodes.YELLOW}Seen {owned.SeenPages.Count}/{owned.PageCount} accessory bag pages, {McColorCodes.GRAY}open the others for better results"));
    }

    protected override string GetId(CheapAccessory elem)
    {
        return elem.ItemName;
    }
}
