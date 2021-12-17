using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class ExperimentalCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            await socket.UpdateSettings(settings =>
            {
                settings.Settings.AllowedFinders = LowPricedAuction.FinderType.FLIPPER | LowPricedAuction.FinderType.SNIPER_MEDIAN | LowPricedAuction.FinderType.SNIPER;
                return settings;
            });
            socket.SendMessage(COFLNET + $"You opted in into experimental flips");
        }
    }

    
}