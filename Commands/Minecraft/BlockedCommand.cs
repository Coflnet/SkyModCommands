using System;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class BlockedCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            if (socket.TopBlocked.Count == 0)
            {
                socket.SendMessage(COFLNET + "No blocked flips found, make sure you don't click this shortly after the 'flips in 10 seconds' message. (the list gets reset when that message appears)");
                return Task.CompletedTask;
            }
            var r = new Random();
            socket.SendMessage(socket.TopBlocked.OrderBy(e => r.Next()).Take(5).SelectMany(b =>
                {
                    socket.Settings.GetPrice(hypixel.FlipperService.LowPriceToFlip(b.Flip), out long targetPrice, out long profit);
                    return new ChatPart[]
                    {
                        new ChatPart(
                        $"{socket.GetRarityColor(b.Flip.Auction.Tier)}{b.Flip.Auction.ItemName}{McColorCodes.GRAY} (+{socket.FormatPrice(profit)}) {McColorCodes.GRAY} blocked because {McColorCodes.WHITE}{b.Reason}",
                        "https://sky.coflnet.com/auction/" + b.Flip.Auction.Uuid,
                        "Open on website"),
                        new ChatPart(
                        $" §l[ah]§r\n",
                        "/viewauction " + b.Flip.Auction.Uuid,
                        "Open in game")
                    };
                }).Append(new ChatPart() { text = COFLNET + "These are random examples of blocked flips.", onClick = "/cofl blocked", hover = "Execute again to get another sample" }).ToArray()
            );
            return Task.CompletedTask;
        }
    }
}