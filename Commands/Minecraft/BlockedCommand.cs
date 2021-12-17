using System;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class BlockedCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (socket.TopBlocked.Count == 0)
            {
                socket.SendMessage(COFLNET + "No blocked flips found, make sure you don't click this shortly after the 'flips in 10 seconds' message. (the list gets reset when that message appears)");
                return;
            }
            var r = new Random();
            socket.SendMessage(socket.TopBlocked.OrderBy(e=>r.Next()).Take(5).Select(b =>
            {
                socket.Settings.GetPrice(hypixel.FlipperService.LowPriceToFlip(b.Flip), out long targetPrice, out long profit);
                return new ChatPart(
                    $"{b.Flip.Auction.ItemName} (+{socket.FormatPrice(profit)}) got blocked because {b.Reason.Replace("SNIPER", "experimental flip finder")}\n",
                    "https://sky.coflnet.com/auction/" + b.Flip.Auction.Uuid,
                    "Click to open");
            })
            .Append(new ChatPart() { text = COFLNET + "These are random examples of blocked flips.", onClick = "/cofl blocked", hover = "Execute again to get another sample" }).ToArray());
        }
    }
}