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
        internal static bool IsBazaarSearch(string searchVal)
        {
            return searchVal == "bazaar" || searchVal == "bz";
        }

        internal static bool MatchesSearch(MinecraftSocket.BlockedElement blocked, string searchVal)
        {
            if (string.IsNullOrWhiteSpace(searchVal))
                return true;
            if (IsBazaarSearch(searchVal))
                return blocked.Flip.Finder == LowPricedAuction.FinderType.Bazaar;
            return $"{blocked.Reason}{blocked.Flip.Auction.ItemName}{blocked.Flip.Auction.Tag}".ToLower().Contains(searchVal.ToLower().Trim());
        }

        internal static string GetDetailsLink(LowPricedAuction flip)
        {
            return flip.Finder == LowPricedAuction.FinderType.Bazaar
                ? "https://sky.coflnet.com/item/" + flip.Auction.Tag
                : "https://sky.coflnet.com/auction/" + flip.Auction.Uuid;
        }

        internal static string GetOpenCommand(LowPricedAuction flip)
        {
            return flip.Finder == LowPricedAuction.FinderType.Bazaar
                ? "/bz " + BazaarUtils.GetSearchValue(flip.Auction.Tag, flip.Auction.ItemName)
                : "/viewauction " + flip.Auction.Uuid;
        }

        internal static string GetOpenLabel(LowPricedAuction flip)
        {
            return flip.Finder == LowPricedAuction.FinderType.Bazaar ? " §l[bz]§r" : " §l[ah]§r";
        }

        internal static string GetOpenHover(LowPricedAuction flip)
        {
            return flip.Finder == LowPricedAuction.FinderType.Bazaar ? "Open in bazaar" : "Open in game";
        }

        internal static bool SupportsFlipOptions(LowPricedAuction flip)
        {
            return flip.Finder != LowPricedAuction.FinderType.Bazaar;
        }

        private static string GetAuctionReasonKey(MinecraftSocket.BlockedElement blocked)
        {
            var auction = blocked.Flip?.Auction;
            var auctionKey = !string.IsNullOrWhiteSpace(auction?.Uuid)
                ? auction.Uuid
                : $"{auction?.Tag}|{auction?.ItemName}";
            return $"{auctionKey}|{blocked.Reason}";
        }

        private class DisplayBlockedElement
        {
            public MinecraftSocket.BlockedElement Blocked = null!;
            public bool HasEstimateRange;
        }

        private static List<DisplayBlockedElement> LimitBlockedOutput(IEnumerable<MinecraftSocket.BlockedElement> blockedElements)
        {
            var result = new List<DisplayBlockedElement>();
            foreach (var group in blockedElements.GroupBy(GetAuctionReasonKey))
            {
                var ordered = group
                    .OrderBy(b => b.Flip.TargetPrice)
                    .ThenByDescending(b => b.Now)
                    .ToList();
                if (ordered.Count == 0)
                    continue;

                var min = ordered.First();
                var max = ordered.Last();
                var hasEstimateRange = min.Flip.TargetPrice != max.Flip.TargetPrice;
                result.Add(new DisplayBlockedElement
                {
                    Blocked = min,
                    HasEstimateRange = hasEstimateRange
                });

                if (!ReferenceEquals(min, max) && hasEstimateRange)
                {
                    result.Add(new DisplayBlockedElement
                    {
                        Blocked = max,
                        HasEstimateRange = true
                    });
                }
            }

            return result.OrderByDescending(r => r.Blocked.Now).ToList();
        }

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
                "which filter matched and remove it" ] },
            { "ShouldSkipFlip", [
                "Either your purse was too low to afford",
                "or the flip was likely already sold",
                "after waiting for fairness delay"
            ]},
            { "purse check", [
                "The recommended item cost more than",
                "your configured purse budget allows.",
                "You can increase max purse usage or",
                "top up your purse to allow this item."
            ]},
            { "bazaar order limit", [
                "You already have too many active bazaar",
                "orders open, so new bazaar recommendations",
                "were paused to avoid filling your inventory.",
                "Claim, cancel, or fill existing orders first."
            ]},
            { "bazaar order already sent", [
                "The same bazaar order with the same",
                "item and price was already suggested.",
                "It won't be suggested again until it",
                "disappears from your bazaar orders upload."
            ]}
            };
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            const int candidateCount = 20;
            const int finalDisplayCount = 10;
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
                flipsToSend = socket.TopBlocked.OrderByDescending(b => b.Flip.TargetPrice - b.Flip.Auction.StartingBid).Take(candidateCount).ToList();
                socket.Dialog(db => db.MsgLine("Blocked flips sorted by profit"));
            }
            else if (IsBazaarSearch(searchVal))
            {
                flipsToSend = socket.TopBlocked.Where(b => b.Flip.Finder == LowPricedAuction.FinderType.Bazaar)
                    .OrderByDescending(b => b.Now)
                    .Take(candidateCount)
                    .ToList();
                if (flipsToSend.Count == 0)
                {
                    socket.SendMessage(COFLNET + "No blocked bazaar recommendations found yet");
                    return;
                }
                socket.Dialog(db => db.MsgLine("Blocked bazaar recommendations"));
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
                else
                    baseCollection = baseCollection.Where(b => MatchesSearch(b, searchVal)).AsQueryable();
                flipsToSend = baseCollection.Take(candidateCount).ToList();
            }
            else
                flipsToSend = GetRandomFlips(socket, candidateCount);

            var displayFlips = LimitBlockedOutput(flipsToSend)
                .Take(finalDisplayCount)
                .ToList();

            var countByReson = socket.TopBlocked.GroupBy(b => b.Reason).Select(g => new { Reason = g.Key, Count = g.Count() }).ToDictionary(g => g.Reason, g => g.Count);
            Activity.Current.Log(JsonConvert.SerializeObject(displayFlips.Select(f => f.Blocked).ToList()));

            socket.SendMessage(displayFlips.SelectMany(display =>
            {
                var b = display.Blocked;
                // add sent flips back to queue so when they are selected for flip options they are still there if they are at the end of the queue
                if (!socket.TopBlocked.OrderByDescending(t => t.Now).Take(300).Contains(b))
                    socket.TopBlocked.Enqueue(b);
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
                        + $"You can enable the {b.Reason.Replace("finder", "")} finder via the website.\nBut be cautious, some finders are experimental"
                        + $"\nand might overvalue estimations.\nCheck the description for each of them.\n{McColorCodes.GRAY}Click this to open algorithm explanation video";
                    clickAction = "https://www.youtube.com/watch?v=nfMo5CeJDgc&list=PLDpPmxIcq9tAssQlyJMBlSmSg5JOpq699&index=9&pp=iAQB";
                }
                var text = $"{McColorCodes.DARK_GRAY}> {formatedName}{McColorCodes.GRAY} (+{socket.FormatPrice(profit)})";
                if (string.IsNullOrEmpty(longReason))
                    longReason = $" {McColorCodes.GRAY} because {McColorCodes.WHITE}{b.Reason}";

                if (display.HasEstimateRange)
                {
                    longReason += $"\n{McColorCodes.DARK_GRAY}Showing min and max estimate only.\nWe run multiple finder instances and valuations can differ slightly.";
                }

                if (!string.IsNullOrEmpty(socket.Settings.ModSettings.BlockedFormat))
                    text = socket.formatProvider.FormatFlip(FlipperService.LowPriceToFlip(b.Flip), b.Reason);
                var detailsLink = GetDetailsLink(b.Flip);
                var openCommand = GetOpenCommand(b.Flip);
                var openLabel = GetOpenLabel(b.Flip);
                var openHover = GetOpenHover(b.Flip);
                var itemHover = b.Flip.Finder == LowPricedAuction.FinderType.Bazaar
                    ? $"{b.Flip.Auction.ItemName}\nClick to open on website"
                    : b.Flip.Auction?.Context?.GetValueOrDefault("lore")
                        + "\nCick to open on website";
                var mainParts = new List<ChatPart>
                {
                    new ChatPart(
                    text,
                    detailsLink,
                    itemHover),
                    new ChatPart(
                    openLabel,
                    openCommand,
                    openHover)
                };
                if (SupportsFlipOptions(b.Flip))
                    mainParts.Add(new ChatPart(" ✥ \n", "/cofl dialog flipoptions " + b.Flip.Auction.Uuid, "Expand flip options"));
                else
                    mainParts.Add(new ChatPart("\n"));
                if (!string.IsNullOrEmpty(longReason))
                {
                    if (countByReson.ContainsKey(b.Reason))
                    {
                        var percent = (int)((countByReson[b.Reason] / (float)socket.TopBlocked.Count) * 100);
                        longReason += $"\n{McColorCodes.YELLOW}This is the reason for {countByReson[b.Reason]} blocked flips ({percent}%)";
                    }
                    if (socket.ModAdapter is AfVersionAdapter)
                        mainParts.Insert(1, new ChatPart($"{McColorCodes.GRAY}[{McColorCodes.RESET}{longReason}{McColorCodes.GRAY}]", clickAction, longReason));
                    else
                        mainParts.Insert(1, new ChatPart($"{McColorCodes.GRAY}[{McColorCodes.RESET}hover for info{McColorCodes.GRAY}]", clickAction, longReason));
                }
                return mainParts;
            }).Append(new ChatPart()
            {
                text = COFLNET + "These are examples of blocked flips. Hover for options",
                onClick = IsBazaarSearch(searchVal) ? "/cofl blocked bazaar" : "/cofl blocked profit",
                hover = $"Execute again to get another sample,\n"
                        + "they are random each time and the most \n"
                        + "common block cause is sorted on top\n"
                    + "same auction + reason is collapsed to min/max estimates\n"
                        + $"Or run {McColorCodes.AQUA}/cofl blocked profit {McColorCodes.RESET} to order by most profit\n"
                    + "Or run " + $"{McColorCodes.AQUA}/cofl blocked <search> {McColorCodes.RESET}to search for specific flips\n"
                    + $"Use {McColorCodes.AQUA}/cofl blocked bazaar {McColorCodes.RESET}for blocked bazaar recommendations",
            }).ToArray());
            var sentCount = socket.LastSent.Where(s => s.Auction.Start > DateTime.UtcNow.AddMinutes(-10)).Count();
            if (sentCount > 2 && socket.LastSent.OrderByDescending(s => s.Auction.Start).Take(10).All(s => !s.AdditionalProps.ContainsKey("clickT")))
                socket.Dialog(db => db.MsgLine($"There were {sentCount} flips sent in the last 10 minutes, but you didn't click any of them.")
                    .If(() => socket.ModAdapter is AfVersionAdapter,
                        b => b.MsgLine("Maybe adjust your settings"),
                        d => d.MsgLine("Make sure none of your other mods are blocking the chat messages.")));
            if (await socket.UserAccountTier() == AccountTier.NONE)
            {
                socket.Dialog(db => db.CoflCommand<PurchaseCommand>($"Note that you don't have premium/prem+, flips will show up very late if at all. Eg. the user finder doesn't work. \n{McColorCodes.GREEN}[Click to change that]", "", "Click to select a premium plan"));
            }

            if (flipsToSend.Count > 1)
            {
                Activity.Current.Log($"Archiving {flipsToSend.Count} blocked flips");
                await socket.GetService<IBlockedService>().ArchiveBlockedFlipsUntil(new(flipsToSend), socket.UserId, 0);
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

        private static List<MinecraftSocket.BlockedElement> GetRandomFlips(MinecraftSocket socket, int count)
        {
            var grouped = socket.TopBlocked.OrderBy(e => Random.Shared.Next()).Take(count).GroupBy(f => f.Reason).OrderByDescending(f => f.Count());
            var flipsToSend = new List<MinecraftSocket.BlockedElement>();
            if (!grouped.Any())
                return flipsToSend;
            flipsToSend.AddRange(grouped.First().Take(Math.Max(1, 7 - grouped.Count())));
            flipsToSend.AddRange(grouped.Skip(1).Select(g => g.First()));
            return flipsToSend;
        }
    }
}