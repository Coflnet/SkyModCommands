using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;
using System.Diagnostics;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Shows you which flips were blocked and why",
        "Usage: /cofl blocked [search]",
        "Use this to find out why you don't get any flips",
        "or didn't get a specific flip",
        "Example: /cofl blocked Hyperion")]
    public class BlockedCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (socket.SessionInfo.IsNotFlipable)
            {
                socket.SendMessage(COFLNET + "You are not in a gamemode that does not have access to the auction house. \n"
                    + "Switch your gamemode/leave dungeons to flip");
                return;
            }
            if (!socket.SessionInfo.FlipsEnabled)
            {
                socket.Dialog(db => db.CoflCommand<FlipCommand>("You don't have flips enabled, as a result there are no flips blocked.\nClick to enable flips", "", "Click to enable them"));
                return;
            }
            if (socket.TopBlocked.Count == 0)
            {
                socket.SendMessage(COFLNET + "No blocked flips found, it can take a while after you connected");
                return;
            }
            List<MinecraftSocket.BlockedElement> flipsToSend;

            if (arguments.Length > 2)
            {
                var searchVal = JsonConvert.DeserializeObject<string>(arguments).ToLower();
                var baseCollection = socket.TopBlocked.AsQueryable(); ;
                Console.WriteLine("found filters " + searchVal);
                if (searchVal.Contains('='))
                {
                    Console.WriteLine("parsing filters");
                    var filters = new Dictionary<string, string>();
                    searchVal = await new FilterParser().ParseFiltersAsync(socket, searchVal, filters, FlipFilter.AllFilters);
                    var filter = new FlipFilter(filters);
                    Console.WriteLine($"remaining filter: {searchVal} filters: {JsonConvert.SerializeObject(filters)}");
                    baseCollection = baseCollection.Where(b => filter.IsMatch(FlipperService.LowPriceToFlip(b.Flip)));
                }
                flipsToSend = baseCollection.Where(b => $"{b.Reason}{b.Flip.Auction.ItemName}{b.Flip.Auction.Tag}".ToLower().Contains(searchVal.ToLower().Trim())).ToList();
            }
            else
                flipsToSend = GetRandomFlips(socket);

            Activity.Current.Log(JsonConvert.SerializeObject(flipsToSend));

            socket.SendMessage(flipsToSend.SelectMany(b =>
            {
                socket.Settings.GetPrice(FlipperService.LowPriceToFlip(b.Flip), out long targetPrice, out long profit);
                return new ChatPart[]
                {
                        new ChatPart(
                        $"{McColorCodes.DARK_GRAY}> {socket.formatProvider.GetRarityColor(b.Flip.Auction.Tier)}{b.Flip.Auction.ItemName}{McColorCodes.GRAY} (+{socket.FormatPrice(profit)}) {McColorCodes.GRAY} because {McColorCodes.WHITE}{b.Reason}",
                        "https://sky.coflnet.com/auction/" + b.Flip.Auction.Uuid,
                        "Open on website"),
                        new ChatPart(
                        $" §l[ah]§r",
                        "/viewauction " + b.Flip.Auction.Uuid,
                        "Open in game"),
                        new ChatPart(" ✥ \n", "/cofl dialog flipoptions " + b.Flip.Auction.Uuid, "Expand flip options")
                };
            }).Append(new ChatPart() { text = COFLNET + "These are examples of blocked flips.", onClick = "/cofl blocked", hover = "Execute again to get another sample" }).ToArray());
            var sentCount = socket.LastSent.Where(s => s.Auction.Start > DateTime.UtcNow.AddMinutes(-10)).Count();
            if (sentCount > 2 && socket.LastSent.OrderByDescending(s => s.Auction.Start).Take(10).All(s => !s.AdditionalProps.ContainsKey("clickT")))
                socket.Dialog(db => db.MsgLine($"There were {sentCount} flips sent in the last 10 minutes, but you didn't click any of them.")
                            .MsgLine("Make sure none of your other mods are blocking the chat messages."));
            if (await socket.UserAccountTier() == AccountTier.NONE)
            {
                socket.Dialog(db => db.CoflCommand<PurchaseCommand>("Note that you don't have premium, flips will show up very late if at all", "", "Click to select a premium plan"));
            }
        }

        private static List<MinecraftSocket.BlockedElement> GetRandomFlips(MinecraftSocket socket)
        {
            var r = new Random();
            var grouped = socket.TopBlocked.OrderBy(e => r.Next()).GroupBy(f => f.Reason).OrderByDescending(f => f.Count());
            var flipsToSend = new List<MinecraftSocket.BlockedElement>();
            flipsToSend.AddRange(grouped.First().Take(7 - grouped.Count()));
            flipsToSend.AddRange(grouped.Skip(1).Select(g => g.First()));
            return flipsToSend;
        }
    }
}