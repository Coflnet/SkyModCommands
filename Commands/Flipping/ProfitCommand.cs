using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{

    [CommandDescription("How much profit you made through flipping",
        "Usage: /cl profit {days}",
        "The default is 7 days",
        "Flip tracking includes modifications to items and craft flips")]
    public class ProfitCommand : McCommand
    {
        private const int MaxDaysHighestTier = 180;

        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = JsonConvert.DeserializeObject<string>(arguments).Split(' ');
            TimeSpan time = await GetTimeSpan(socket, arguments, args);
            // replace this call with stored socket.sessionLifesycle.AccountInfo.Value.McIds

            IEnumerable<string> accounts = await GetAccounts(socket, args);
            Task<Dictionary<string, string>> namesTask = GetNames(socket, accounts);
            var response = await socket.GetService<FlipTrackingService>().GetPlayerFlips(accounts, time);
            var who = "you";
            if (args.Length > 1) // except last arg
                who = string.Join(" ", args.Take(args.Length - 1));
            if (response.Flips.Count() == 0)
            {
                socket.Dialog(db => db.MsgLine($"Sorry we don't have any tracked flips for {who} yet", hover: "Flips only count after they have sold"));
                return;
            }
            string hover = GetHoverText(socket, response);
            var perAccount = response.Flips.GroupBy(f => f.Seller).ToDictionary(g => g.Key, g => g.Sum(f => f.Profit));
            var names = namesTask == null ? null : await namesTask;
            var accountHover = "you have only one account";
            if (names != null)
                accountHover = string.Join("\n", names?.Select(a => $"- {a.Value}: {McColorCodes.AQUA}{FormatPrice(socket, perAccount.GetValueOrDefault(a.Key))}"));
            var paidSum = response.Flips.Sum(f => f.PricePaid);
            socket.Dialog(db => db.Msg($"According to our data {who} made {FormatPrice(socket, response.TotalProfit)} "
                + $"in the last {McColorCodes.AQUA}{time.TotalDays}{McColorCodes.GRAY} days across {FormatPrice(socket, response.Flips.Length)} auctions", null, hover)
                .If(() => accounts.Count() > 1, db => db.Msg($" across your {accounts.Count()} accounts", null, accountHover))
                 .Msg($"\n{who} spent {FormatPrice(socket, paidSum)} with an average {FormatPrice(socket, (long)response.Flips.Sum(f => f.Profit) * 100 / (paidSum == 0 ? 1 : paidSum))}% profit margin",
                null, hover));
            var sorted = response.Flips.OrderByDescending(f => f.Profit).ToList();
            var best = sorted.FirstOrDefault();
            var worst = sorted.LastOrDefault();
            if (best == null)
                return;
            socket.SendMessage($"{COFLNET} The best flip was a {FormatFlipName(socket, best)}" +
                            FormatFlip(socket, best),
                "https://sky.coflnet.com/auction/" + best.OriginAuction,
                $"Click to open best flip purchase\n{McColorCodes.GRAY}The worst flip was \n" + FormatFlip(socket, worst));


        }
        public static string FormatFlip(MinecraftSocket socket, FlipDetails best)
        {
            return $" {FormatPrice(socket, best.PricePaid)} -> {FormatPrice(socket, best.SoldFor)} (+{FormatPrice(socket, best.Profit)})";
        }

        public static string FormatFlipName(MinecraftSocket socket, FlipDetails best)
        {
            return $"{socket.formatProvider.GetRarityColor(Enum.Parse<Tier>(best.Tier.Replace("VERYSPECIAL", "VERY_SPECIAL")))}{best.ItemName}";
        }

        protected static Task<Dictionary<string, string>> GetNames(MinecraftSocket socket, IEnumerable<string> accounts)
        {
            Task<Dictionary<string, string>> namesTask = null;
            if (accounts.Count() > 1)
            {
                var nameService = socket.GetService<IPlayerNameApi>();
                namesTask = nameService.PlayerNameNamesBatchPostAsync(accounts.ToList());
            }

            return namesTask;
        }

        protected static async Task<IEnumerable<string>> GetAccounts(MinecraftSocket socket, string[] args)
        {
            IEnumerable<string> accounts;
            if (args.Length > 1)
                accounts = new string[] { await socket.GetPlayerUuid(args.First()) };
            else
                accounts = await socket.sessionLifesycle.GetMinecraftAccountUuids();
            return accounts;
        }

        protected async Task<TimeSpan> GetTimeSpan(MinecraftSocket socket, string arguments, string[] args)
        {
            int maxDays = await GetMaxDaysPossible(socket);
            if (!double.TryParse(args.Last(), out var days) && args.First() != "")
            {
                var className = GetType().Name.Replace("Command", "").ToLower();
                throw new CoflnetException("invalid_usage", $"usage /cofl {className} [ign] {{0.5-{maxDays}}}");
            }
            else if (arguments.Length <= 2)
            {
                days = 7;
                socket.Dialog(db => db.MsgLine($"Using the default of {days} days because you didn't specify a number"));
            }
            var time = TimeSpan.FromDays(days);
            if (time > TimeSpan.FromDays(maxDays))
            {
                socket.Dialog(db => db.MsgLine($"sorry the maximum is a {maxDays} days currently. Setting time to {maxDays} days"));
                if (maxDays < MaxDaysHighestTier)
                    socket.Dialog(db => db.CoflCommand<PurchaseCommand>(
                        $"you can upgrade to premium plus to get {MaxDaysHighestTier} days", "premium_plus", "upgrade"));
                time = TimeSpan.FromDays(maxDays);
            }
            else
            {
                socket.SendMessage(COFLNET + "Crunching the latest numbers for you :)", null, "this might take a few seconds");
            }
            return time;
        }

        private static async Task<int> GetMaxDaysPossible(MinecraftSocket socket)
        {
            return (await socket.UserAccountTier()) switch
            {
                >= AccountTier.PREMIUM_PLUS => MaxDaysHighestTier,
                _ => 14
            };
        }

        private string GetHoverText(MinecraftSocket socket, FlipSumary response)
        {
            var tfm = GetProfitForFinder(response, LowPricedAuction.FinderType.TFM);
            var stonks = GetProfitForFinder(response, LowPricedAuction.FinderType.STONKS);
            var other = GetProfitForFinder(response, LowPricedAuction.FinderType.EXTERNAL);
            var coflnet = response.Flips.Where(f => f.Finder == LowPricedAuction.FinderType.FLIPPER
                                                || f.Finder == LowPricedAuction.FinderType.SNIPER
                                                || f.Finder == LowPricedAuction.FinderType.SNIPER_MEDIAN)
                                                .Sum(f => f.SoldFor - f.PricePaid);
            var word = response.TotalProfit > 10_000_000 ? "WOOHOOO" : "total of";
            var hover = $"{word} {FormatPrice(socket, response.TotalProfit)} coins";
            if (tfm > 0)
                hover += $"\n {FormatPrice(socket, tfm)} from {McColorCodes.GOLD} TFM{McColorCodes.GRAY}";
            if (stonks > 0)
                hover += $"\n {FormatPrice(socket, stonks)} from Stonks";
            if (other > 0)
                hover += $"\n {FormatPrice(socket, other)} from other finders";
            if (tfm > 0 || stonks > 0)
                hover += $"\n {FormatPrice(socket, coflnet)} from the {COFLNET} mod";
            return hover;
        }

        private static long GetProfitForFinder(FlipSumary response, LowPricedAuction.FinderType type)
        {
            return response.Flips.Where(f => f.Finder == type).Sum(f => f.Profit);
        }

        private static string FormatPrice(MinecraftSocket socket, long number)
        {
            return $"{McColorCodes.AQUA}{socket.FormatPrice(number)}{McColorCodes.GRAY}";
        }
    }
}