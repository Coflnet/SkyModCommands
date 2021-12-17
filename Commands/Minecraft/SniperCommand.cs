using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class SniperCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            await socket.UpdateSettings(settings =>
            {
                settings.Settings.AllowedFinders = LowPricedAuction.FinderType.SNIPER;
                return settings;
            });
            socket.SendMessage(COFLNET + $"You enabled the super secret sniper mode :O");
        }
    }
}