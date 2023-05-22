using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.MC;

public class ItemInfoCommand : ItemSelectCommand<ItemInfoCommand>
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        await HandleSelectionOrDisplaySelect(socket, args, socket.SessionInfo.McName, "Get about this item: \n");
    }

    protected override async Task SelectedItem(MinecraftSocket socket, string targetPlayer, Item item)
    {
        var auction = new SaveAuction()
        {
            Tag = item.Tag,
            ItemName = item.ItemName,
            Enchantments = item.Enchantments.Select(e => new Enchantment()
            {
                Type = Enum.Parse<Enchantment.EnchantmentType>(e.Key, true),
                Level = (byte)e.Value
            }).ToList(),
            Count = item.Count ?? 1,
        };
        auction.SetFlattenedNbt(NBT.FlattenNbtData(item.ExtraAttributes));
        var res = await socket.GetService<Coflnet.Sky.Commands.Shared.ISniperClient>().GetPrices(new SaveAuction[] { auction });
        var price = res[0].Median;
        var priceString = price == 0 ? "§cnot found" : $"§a{price} coins";
        socket.Dialog(db => db.MsgLine($"§7[§6§lItem Info§7]§r\n{item.ItemName}\n{item.Description}")
            .MsgLine($"Median price: {priceString}")
            .MsgLine($"Lowest Bin: {res[0].Lbin.Price} (click to get auction)", $"https://sky.coflnet.com/a/{socket.GetService<AuctionService>().GetUuid(res[0].Lbin.AuctionId)}", "click to open auction")
        .CoflCommand<ItemInfoCommand>("", targetPlayer, "get another item"));
    }
}
