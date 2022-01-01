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

            var response = await Sky.Commands.FlipTrackingService.Instance.GetPlayerFlips(socket.McUuid, time);
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
                hover += $"\n {FormatPrice(socket, other)} from the {COFLNET} mod";
            socket.SendMessage(COFLNET + $"According to our data you made {McColorCodes.AQUA}{socket.FormatPrice(response.TotalProfit)}{McColorCodes.GRAY} "
                + $"in the last {McColorCodes.AQUA}{time.TotalDays}{McColorCodes.GRAY} days accross {McColorCodes.AQUA}{response.Flips.Length}{McColorCodes.GRAY} auctions",
                null,hover);
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