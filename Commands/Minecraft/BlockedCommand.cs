using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Coflnet.Sky.Commands.Shared;

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
            var grouped = socket.TopBlocked.OrderBy(e => r.Next()).GroupBy(f=>f.Reason).OrderByDescending(f=>f.Count());
            var flipsToSend = new List<MinecraftSocket.BlockedElement>();
            flipsToSend.AddRange(grouped.First().Take(5 - grouped.Count()));
            flipsToSend.AddRange(grouped.Skip(1).Select(g=>g.First()));
            socket.SendMessage(flipsToSend.Take(5).SelectMany(b =>
                {
                    socket.Settings.GetPrice(FlipperService.LowPriceToFlip(b.Flip), out long targetPrice, out long profit);
                    return new ChatPart[]
                    {
                        new ChatPart(
                        $"{McColorCodes.DARK_GRAY}> {socket.formatProvider.GetRarityColor(b.Flip.Auction.Tier)}{b.Flip.Auction.ItemName}{McColorCodes.GRAY} (+{socket.FormatPrice(profit)}) {McColorCodes.GRAY} because {McColorCodes.WHITE}{b.Reason}",
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