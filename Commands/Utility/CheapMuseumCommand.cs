using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class CheapMuseumCommand : McCommand
{
    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var service = socket.GetService<MuseumService>();
        var cheapMuseum = await service.GetBestMuseumPrices();

        socket.Dialog(db => db.MsgLine("The cheapest museum prices are:")
            .ForEach(cheapMuseum, (db, item) =>
                db.MsgLine($" {item.ItemName} for {McColorCodes.AQUA}{item.PricePerExp} coins {McColorCodes.GRAY}per exp",
                    "/viewauction " + item.AuctuinUuid, "Click to view the auction")));
    }
}