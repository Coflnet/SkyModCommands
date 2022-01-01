using System;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class ProfitCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if(!double.TryParse(arguments.Trim('"'),out double days))
            {
                socket.SendMessage(COFLNET + "usage /cofl profit {amountOfDays}");
                return;
            }
            var time = TimeSpan.FromDays(days);
            if (time > TimeSpan.FromDays(2))
            {
                socket.SendMessage(COFLNET + "sorry the maximum is two days currently");
                time = TimeSpan.FromDays(2);
            }

            var response = await Sky.Commands.FlipTrackingService.Instance.GetPlayerFlips(socket.McUuid, time);

            socket.SendMessage(COFLNET + $"According to our data you made {McColorCodes.AQUA}{socket.FormatPrice(response.TotalProfit)}{McColorCodes.GRAY} " 
                + $"in the last {McColorCodes.AQUA}{time.TotalDays}{McColorCodes.GRAY} days accross {McColorCodes.AQUA}{response.Flips.Length}{McColorCodes.GRAY} auctions",
                null,
                $"whohoo {McColorCodes.AQUA}{socket.FormatPrice(response.TotalProfit)}{McColorCodes.GRAY} coins");
        }
    }
}