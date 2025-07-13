using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;

public class ItemInfoCommand : ItemSelectCommand<ItemInfoCommand>
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        await HandleSelectionOrDisplaySelect(socket, args, socket.SessionInfo.McName, "Get about this item: \n");
    }

    protected override async Task SelectedItem(MinecraftSocket socket, string targetPlayer, PlayerState.Client.Model.Item item)
    {
        SaveAuction auction = ConvertToAuction(item);
        var res = await socket.GetService<Shared.ISniperClient>().GetPrices(new SaveAuction[] { auction });
        var price = res[0].Median;
        var priceString = price == 0 ? "§cnot found" : $"§a{price} coins";
        socket.Dialog(db => db.MsgLine($"§7[§6§lItem Info§7]§r\n{item.ItemName}\n{item.Description}")
            .MsgLine($"Median price: {priceString}")
            .MsgLine($"Lowest Bin: {res[0].Lbin.Price} (click to get auction)", $"https://sky.coflnet.com/a/{socket.GetService<AuctionService>().GetUuid(res[0].Lbin.AuctionId)}", "click to open auction")
        .CoflCommand<ItemInfoCommand>("", targetPlayer, "get another item"));
    }
}
