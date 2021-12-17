using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class NormalCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            await socket.UpdateSettings(settings =>
            {
                settings.Settings.AllowedFinders = LowPricedAuction.FinderType.FLIPPER;
                return settings;
            });
            socket.SendMessage(COFLNET + $"You went back to normal flipper mode again");
        }
    }
}