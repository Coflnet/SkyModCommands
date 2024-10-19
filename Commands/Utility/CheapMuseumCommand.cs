using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class CheapMuseumCommand : ReadOnlyListCommand<MuseumService.Cheapest>
{
    public override bool IsPublic => true;

    protected override void Format(MinecraftSocket socket, DialogBuilder db, MuseumService.Cheapest item)
    {
        db.MsgLine($" {item.ItemName} for {McColorCodes.AQUA}{item.PricePerExp} coins {McColorCodes.GRAY}per exp",
                    "/viewauction " + item.AuctuinUuid, "Click to view the auction");
    }

    protected override async Task<IEnumerable<MuseumService.Cheapest>> GetElements(MinecraftSocket socket, string val)
    {
        var service = socket.GetService<MuseumService>();
        var profileClient = socket.GetService<IProfileClient>();
        var tier = socket.SessionInfo.SessionTier;
        var amount = tier switch
        {
            AccountTier.PREMIUM_PLUS => 1000,
            AccountTier.PREMIUM => 500,
            AccountTier.STARTER_PREMIUM => 100,
            _ => 30
        };
        var age = tier switch
        {
             > AccountTier.STARTER_PREMIUM => DateTime.UtcNow - TimeSpan.FromMinutes(10),
            _ => DateTime.UtcNow - TimeSpan.FromHours(4)
        };
        var alreadDonated = await profileClient.GetAlreadyDonatedToMuseum(socket.SessionInfo.McUuid, "current", age);
        return await service.GetBestMuseumPrices(alreadDonated,amount);
    }

    protected override DialogBuilder PrintResult(MinecraftSocket socket, string title, int page, IEnumerable<MuseumService.Cheapest> toDisplay, int totalPages)
    {
        return base.PrintResult(socket, title, page, toDisplay, totalPages)
            .If(() => socket.SessionInfo.SessionTier < AccountTier.PREMIUM_PLUS && page > 1,
                db => db.CoflCommand<PurchaseCommand>($"With {McColorCodes.GOLD}prem+{McColorCodes.RESET} you can see the {McColorCodes.AQUA}top 1000", "premium_plus", "Click to upgrade"));
    }

    protected override string GetId(MuseumService.Cheapest elem)
    {
        return elem.ItemName;
    }
}