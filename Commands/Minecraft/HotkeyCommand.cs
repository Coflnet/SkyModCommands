using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class HotkeyCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var parts = Convert<string>(arguments).Split('|');
            if (parts[0] == "upload_item" && parts.Length == 1)
            {
                socket.Dialog(db => db.MsgLine("Please hold an item to use this command with"));
                return;
            }
            var auction = new SaveAuction();
            var nbt = NBT.FillDetails(auction, parts[1], true);
            var lore = string.Join("\n", NBT.GetLore(nbt));
            auction.Context["lore"] = lore;
            socket.SessionInfo.SelectedItem = auction;
            socket.Dialog(db => db.MsgLine($"Item received {auction.ItemName}", null, auction.Context["lore"]));

            var sniperService = socket.GetService<ISniperClient>();
            var values = await sniperService.GetPrices([auction]);
            var price = values.First();
            var instaSell = SniperClient.InstaSellPrice(price);
            var formattedInstasell = socket.FormatPrice(instaSell.Item1);
            socket.Dialog(db => db.MsgLine($"The value of this item is {McColorCodes.AQUA}{socket.FormatPrice(price.Median)}", null,
                            $"Took into account these modifiers:\n{price.MedianKey}")
                .MsgLine($"Lowest bin sits at {McColorCodes.AQUA}{socket.FormatPrice(price.Lbin.Price)}")
                .Msg($"To sell quickly list at {McColorCodes.AQUA}{formattedInstasell}", $"copy:{formattedInstasell}", "click to copy")
                    .MsgLine($"{McColorCodes.GRAY}[put into chat]", $"suggest:{formattedInstasell}", "click to put \nsuggestion into chat"));
        }
    }
}