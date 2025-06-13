using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;
using System.Diagnostics;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.FlipTracker.Client.Api;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Shows you which flips were blocked and why",
        "Usage: /cofl blocked [search]",
        "Use this to find out why you don't get any flips",
        "or didn't get a specific flip",
        "Example: /cofl blocked Hyperion",
        "Also supports 'profit' to show sorted by profit",
        "And /cofl blocked <uuid> for specific auctions")]
    public class BlockedCommand : McCommand
    {
        public override bool IsPublic => true;
        private static Dictionary<string, string[]> ReasonLookup = new Dictionary<string, string[]>()
        {
            { "sold", new string[]{
                "the flip was already sold when",
                "it was about to be sent to you",
                "This usually is the case when you don't",
                "have prem+ or high fairness delay" } },
            { "profit Perentage", new string[]{
                "Your minProfitPercent is higher than",
                "the profit percentage of the flip.",
                "You could try lowering that setting",
                $"Eg. {McColorCodes.AQUA}/cofl set minProfitPercent 5" } },
            { "minProfit", new string[]{
                "Your minProfit is higher than",
                "the profit of the flip.",
                "You could try lowering that setting.",
                $"Eg. {McColorCodes.AQUA}/cofl set minProfit 1m" } },
            { "minVolume", new string[]{
                "Your minVolume setting is higher than",
                "the volume (sales per day) of the flip.",
                "You could try lowering that setting.",
                $"Eg. {McColorCodes.AQUA}/cofl set minVolume 1" } },
            { "high competition", new string[]{
                "Your settings state that you want to avoid",
                "high competition flips to be able to buy ",
                "more of the flips you see.",
                "You can disable that setting.",
                $"Eg. {McColorCodes.AQUA}/cofl set blockhighcompetition false" } },
            { "forced blacklist matched color filter", [
                "A force blacklist entrie in your config",
                "for armor matched this flip",
                "You can use the flip menu (✥) to find",
                "which filter matched and remove it" ] },
            { "forced blacklist matched pet filter", [
                "A force blacklist entrie in your config",
                "for pets matched this flip.",
                "You can use the flip menu (✥) to find",
                "which filter matched and remove it" ] },
            { "forced blacklist matched general filter", [
                "A force blacklist entrie in your config",
                "matched this flip and blocked it.",
                "You can use the flip menu (✥) to find",
                "which filter matched and remove it" ] },
            { "forced blacklist for", [
                "A force blacklist entrie in your config",
                "matched this flip and blocked it.",
                "You can use the flip menu (✥) to find",
                "which filter matched and remove it" ] },
            { "blacklist for", [
                "A blacklist entrie in your config",
                "matched this flip and blocked it.",
                "You can use the flip menu (✥) to find",
                "which filter matched and remove it" ] }
            };
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var searchVal = JsonConvert.DeserializeObject<string>(arguments)?.ToLower();

            if (Guid.TryParse(searchVal, out var auctionUUid))
            {
                await SendBlockedAuction(socket, searchVal, auctionUUid);
                return;
            }
            if (socket.SessionInfo.IsNotFlipable)
            {
                socket.SendMessage(COFLNET + "You are not in a gamemode that does have access to the auction house. \n"
                    + "Switch your gamemode/leave dungeons to flip");
                return;
            }
            if (!socket.SessionInfo.FlipsEnabled)
            {
                socket.Dialog(db => db.CoflCommand<FlipCommand>("You don't have flips enabled, as a result there are no flips blocked.\nClick to enable flips", "", "Click to enable them"));
                return;
            }
            if (socket.SessionInfo.Purse == -1)
            {
                socket.Dialog(db => db.MsgLine("It looks like you are not in skyblock currently. Flips are only shown in skyblock."));
                return;
            }
            if (socket.TopBlocked.Count == 0)
            {
                socket.SendMessage(COFLNET + "No blocked flips found, it can take a while after you connected");
                return;
            }
            if (socket.Settings.ModSettings.AhDataOnlyMode && socket.ModAdapter is not AfVersionAdapter)
            {
                socket.Dialog(db => db.CoflCommand<FlipCommand>("You are in ah data only mode. Use /cofl flip to enable flips or /cofl flip always to always autostart the flipper", "", "Click to enable flips"));
                return;
            }
            if (socket.Settings.DisableFlips && socket.ModAdapter is not AfVersionAdapter)
            {
                socket.Dialog(db => db.MsgLine("You turned flipping off. To turn it on do /cofl flip always"));
                return;
            }
            if (socket.HasFlippingDisabled())
            {
                socket.Dialog(db => db.MsgLine("You seem to have flipping disabled, not sure how, please make a report with '/cofl report' and a thead on our discord."));
                return;
            }
            List<MinecraftSocket.BlockedElement> flipsToSend;

            if (searchVal == "profit")
            {
                flipsToSend = socket.TopBlocked.OrderByDescending(b => b.Flip.TargetPrice - b.Flip.Auction.StartingBid).Take(10).ToList();
                socket.Dialog(db => db.MsgLine("Blocked flips sorted by profit"));
            }
            else if (arguments.Length > 2)
            {
                var baseCollection = socket.TopBlocked.AsQueryable();
                if (searchVal.Contains('='))
                {
                    var filters = new Dictionary<string, string>();
                    searchVal = await new FilterParser().ParseFiltersAsync(socket, searchVal, filters, FlipFilter.AllFilters);
                    var filter = new FlipFilter(filters, socket.SessionInfo);
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
                var formatedName = socket.formatProvider.GetRarityColor(b.Flip.Auction.Tier) + socket.formatProvider.GetItemName(b.Flip.Auction);
                var longReason = "";
                var matchingReason = ReasonLookup.Keys.FirstOrDefault(r => b.Reason.StartsWith(r));
                string clickAction = null;
                if (matchingReason != default)
                {
                    longReason = string.Join("\n", ReasonLookup[matchingReason]);
                }
                else if (b.Reason.StartsWith("finder"))
                {
                    longReason = "You don't have the algorithm that found this flip enabled.\n"
                        +$"You can enable the {b.Reason.Replace("finder","")} finder via the website.\nBut be cautious, some finders are experimental"
                        + $"\nand might overvalue estimations.\nCheck the description for each of them.\n{McColorCodes.GRAY}Click this to open algorithm explanation video";
                    clickAction = "https://www.youtube.com/watch?v=nfMo5CeJDgc&list=PLDpPmxIcq9tAssQlyJMBlSmSg5JOpq699&index=9&pp=iAQB";
                }
                var text = $"{McColorCodes.DARK_GRAY}> {formatedName}{McColorCodes.GRAY} (+{socket.FormatPrice(profit)})";
                if (string.IsNullOrEmpty(longReason))
                    text += $" {McColorCodes.GRAY} because {McColorCodes.WHITE}{b.Reason}";

                if (!string.IsNullOrEmpty(socket.Settings.ModSettings.BlockedFormat))
                    text = socket.formatProvider.FormatFlip(FlipperService.LowPriceToFlip(b.Flip), b.Reason);
                var mainParts = new List<ChatPart>
                {
                    new ChatPart(
                    text,
                    "https://sky.coflnet.com/auction/" + b.Flip.Auction.Uuid,
                    b.Flip.Auction?.Context?.GetValueOrDefault("lore")
                    + "\nCick to open on website"),
                    new ChatPart(
                    $" §l[ah]§r",
                    "/viewauction " + b.Flip.Auction.Uuid,
                    "Open in game"),
                    new ChatPart(" ✥ \n", "/cofl dialog flipoptions " + b.Flip.Auction.Uuid, "Expand flip options")
                };
                if (!string.IsNullOrEmpty(longReason))
                    if (socket.ModAdapter is AfVersionAdapter)
                        mainParts.Insert(1, new ChatPart($"{McColorCodes.GRAY}[{McColorCodes.RESET}{longReason}{McColorCodes.GRAY}]", clickAction, longReason));
                    else
                        mainParts.Insert(1, new ChatPart($"{McColorCodes.GRAY}[{McColorCodes.RESET}hover for info{McColorCodes.GRAY}]", clickAction, longReason));

                return mainParts;
            }).Append(new ChatPart() { text = COFLNET + "These are examples of blocked flips.", onClick = "/cofl blocked", hover = "Execute again to get another sample" }).ToArray());
            var sentCount = socket.LastSent.Where(s => s.Auction.Start > DateTime.UtcNow.AddMinutes(-10)).Count();
            if (sentCount > 2 && socket.LastSent.OrderByDescending(s => s.Auction.Start).Take(10).All(s => !s.AdditionalProps.ContainsKey("clickT")))
                socket.Dialog(db => db.MsgLine($"There were {sentCount} flips sent in the last 10 minutes, but you didn't click any of them.")
                            .MsgLine("Make sure none of your other mods are blocking the chat messages."));
            if (await socket.UserAccountTier() == AccountTier.NONE)
            {
                socket.Dialog(db => db.CoflCommand<PurchaseCommand>($"Note that you don't have premium, flips will show up very late if at all. Eg. the user finder doesn't work. \n{McColorCodes.GREEN}[Click to change that]", "", "Click to select a premium plan"));
            }

            if (socket.SessionInfo.Purse != 0 && socket.SessionInfo.Purse < 10_000_000)
            {
                await Task.Delay(2000);
                socket.Dialog(db => db.MsgLine("You don't have many coins in your purse. Flips you can't afford aren't shown."));
            }
            else if (socket.sessionLifesycle.AccountSettings.Value.LoadedConfig != null && socket.sessionLifesycle.CurrentDelay <= TimeSpan.FromMilliseconds(50))
            {
                var averageProfit = socket.LastSent.Select(l => l.TargetPrice - l.Auction.StartingBid).DefaultIfEmpty(0).Average();
                if (socket.Settings?.MinProfit > 2_000_000 && averageProfit > 8_000_000)
                    socket.Dialog(db => db.MsgLine("You seem to have a pretty restrictive config that only shows you flips with high competition. Consider lowering your min profit."));
            }
        }

        private async Task SendBlockedAuction(MinecraftSocket socket, string searchVal, Guid auctionUUid)
        {
            var blockedService = socket.GetService<IBlockedService>();
            var blocked = (await blockedService.GetBlockedReasons(socket.UserId, auctionUUid)).ToList();
            if (blocked.Count() == 0)
            {
                if (socket.SessionInfo.SessionTier <= AccountTier.PREMIUM)
                {
                    socket.Dialog(db => db.CoflCommand<FlipCommand>("You don't have prem+, only prem+ user blocked reasons are stored. \nthis is due to the added cost of storing that data.\nClick to change that", "prem+", "Click to purchase prem+"));
                    return;
                }
                var auction = await AuctionService.Instance.GetAuctionAsync(searchVal);
                if (auction.End > DateTime.Now.AddDays(-6))
                    if (socket.LastSent.Where(s => Guid.Parse(s.Auction.Uuid) == auctionUUid).Any())
                        socket.SendMessage(COFLNET + "Flip wasn't blocked, it was sent to you");
                    else
                        socket.SendMessage(COFLNET + "No blocked reason recorded for this auction. Maybe not found as a flip");
                else
                    socket.SendMessage(COFLNET + "No blocked reason recorded for this auction. It happened too long ago");
                return;
            }
            socket.Dialog(db => db.ForEach(blocked, (db, b) => db.MsgLine($"{b.FinderType} {McColorCodes.GRAY}blocked for {McColorCodes.RESET}{b.Reason}", null, $"At {b.BlockedAt}")));

            var auctionInstance = await AuctionService.Instance.GetAuctionAsync(searchVal, au => au.Include(a => a.Enchantments).Include(a => a.NbtData));
            if (auctionInstance == null)
                return;

            var trackApi = socket.GetService<ITrackerApi>();
            var flipData = await trackApi.GetFlipAsync(auctionInstance.Uuid);
            var estimates = await trackApi.GetFlipsOfAuctionAsync(auctionInstance.UId);
            var toTest = flipData.FirstOrDefault();
            float volumeEstimate = 1;
            if (toTest.Context.TryGetValue("oldRef", out var oldRef) && toTest.Context.TryGetValue("refCount", out var refCount))
            {
                volumeEstimate = int.Parse(refCount) / float.Parse(oldRef) + 1f;
            }

            var lowPricedMock = new LowPricedAuction()
            {
                Auction = auctionInstance,
                TargetPrice = (long)(estimates?.Where(e => e.FinderType == toTest.Finder).Select(e => e.TargetPrice).DefaultIfEmpty(0).Average() ?? auctionInstance.StartingBid),
                DailyVolume = volumeEstimate,
                AdditionalProps = toTest?.Context ?? new(),
                Finder = toTest == null ? LowPricedAuction.FinderType.USER : Enum.Parse<LowPricedAuction.FinderType>(toTest.Finder.ToString())
            };
            socket.LastSent.Enqueue(lowPricedMock);

            await WhichBLEntryCommand.Execute(socket, new WhichBLEntryCommand.Args()
            {
                Uuid = auctionInstance.Uuid,
                WL = false
            });
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