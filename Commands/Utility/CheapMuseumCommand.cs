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
        return await service.GetBestMuseumPrices();
    }

    protected override string GetId(MuseumService.Cheapest elem)
    {
        return elem.ItemName;
    }
}