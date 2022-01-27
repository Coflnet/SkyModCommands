using System;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class ProfitCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (!double.TryParse(arguments.Trim('"'), out double days))
            {
                socket.SendMessage(COFLNET + "usage /cofl profit {0.5-7}");
                return;
            }
            var time = TimeSpan.FromDays(days);
            if (time > TimeSpan.FromDays(7))
            {
                socket.SendMessage(COFLNET + "sorry the maximum is a week currently");
                time = TimeSpan.FromDays(7);
            }
            else
            {
                socket.SendMessage(COFLNET + "Crunching the latest numbers for you :)", null, "this might take a few seconds");
            }

            var response = await Sky.Commands.FlipTrackingService.Instance.GetPlayerFlips(socket.SessionInfo.McUuid, time);
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
            socket.SendMessage(COFLNET + $"According to our data you made {FormatPrice(socket, response.TotalProfit)} "
                + $"in the last {McColorCodes.AQUA}{time.TotalDays}{McColorCodes.GRAY} days accross {FormatPrice(socket, response.Flips.Length)} auctions",
                null, hover);
            var sorted = response.Flips.OrderByDescending(f => f.SoldFor - f.PricePaid).ToList();
            var best = sorted.FirstOrDefault();
            if (best == null)
                return;
            socket.SendMessage(COFLNET + $"The best flip was a {socket.formatProvider.GetRarityColor(Enum.Parse<hypixel.Tier>(best.Tier))}{best.ItemName}" +
                            $" {FormatPrice(socket, best.PricePaid)} -> {FormatPrice(socket, best.SoldFor)}",
                "https://sky.coflnet.com/auction/" + best.OriginAuction, "open origin auction");
        }

        private static long GetProfitForFinder(Shared.FlipSumary response, LowPricedAuction.FinderType type)
        {
            return response.Flips.Where(f => f.Finder == type).Sum(f => f.SoldFor - f.PricePaid);
        }

        private string FormatPrice(MinecraftSocket socket, long number)
        {
            return $"{McColorCodes.AQUA}{socket.FormatPrice(number)}{McColorCodes.GRAY}";
        }

    }
}