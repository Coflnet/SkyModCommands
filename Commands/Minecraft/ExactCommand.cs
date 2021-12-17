using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class ExactCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            await socket.UpdateSettings(settings =>
            {
                settings.Settings.AllowedFinders = LowPricedAuction.FinderType.SNIPER | LowPricedAuction.FinderType.SNIPER_MEDIAN;
                return settings;
            });
            socket.SendMessage(COFLNET + $"You enabled the exact flip mode, this is experimental");
        }
    }
}