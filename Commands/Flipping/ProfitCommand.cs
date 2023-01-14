using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class ProfitCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var maxDays = 14;
            if (!double.TryParse(arguments.Trim('"'), out double days))
            {
                socket.SendMessage(COFLNET + $"usage /cofl profit {{0.5-{maxDays}}}");
                return;
            }
            var time = TimeSpan.FromDays(days);
            if (time > TimeSpan.FromDays(maxDays))
            {
                socket.SendMessage(COFLNET + "sorry the maximum is a week currently");
                time = TimeSpan.FromDays(maxDays);
            }
            else
            {
                socket.SendMessage(COFLNET + "Crunching the latest numbers for you :)", null, "this might take a few seconds");
            }
            // replace this call with stored socket.sessionLifesycle.AccountInfo.Value.McIds

            var accounts = await socket.sessionLifesycle.GetMinecraftAccountUuids();

            var response = await socket.GetService<FlipTrackingService>().GetPlayerFlips(accounts, time);
            if (response.Flips.Count() == 0)
            {
                socket.Dialog(db => db.MsgLine("Sorry we don't have any tracked flips for you yet"));
                return;
            }
            string hover = GetHoverText(socket, response);
            socket.SendMessage(COFLNET + $"According to our data you made {FormatPrice(socket, response.TotalProfit)} "
                + $"in the last {McColorCodes.AQUA}{time.TotalDays}{McColorCodes.GRAY} days across {FormatPrice(socket, response.Flips.Length)} auctions"
                + (accounts.Count() > 1 ? $" across your {accounts.Count()} accounts" : "")
                + $"\nYou spent {FormatPrice(socket, response.Flips.Sum(f => f.PricePaid))} with an average {FormatPrice(socket, (long)response.Flips.Average(f => f.Profit / f.PricePaid))}% profit margin",
                null, hover);
            var sorted = response.Flips.OrderByDescending(f => f.Profit).ToList();
            var best = sorted.FirstOrDefault();
            if (best == null)
                return;
            socket.SendMessage(COFLNET + $"The best flip was a {socket.formatProvider.GetRarityColor(Enum.Parse<Tier>(best.Tier))}{best.ItemName}" +
                            $" {FormatPrice(socket, best.PricePaid)} -> {FormatPrice(socket, best.SoldFor)} (+{FormatPrice(socket, best.Profit)})",
                "https://sky.coflnet.com/auction/" + best.OriginAuction, "open origin auction");
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

        private static long GetProfitForFinder(Shared.FlipSumary response, LowPricedAuction.FinderType type)
        {
            return response.Flips.Where(f => f.Finder == type).Sum(f => f.Profit);
        }

        private string FormatPrice(MinecraftSocket socket, long number)
        {
            return $"{McColorCodes.AQUA}{socket.FormatPrice(number)}{McColorCodes.GRAY}";
        }

    }
}