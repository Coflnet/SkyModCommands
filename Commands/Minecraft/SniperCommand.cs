using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class FastCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var premPlus = $"{McColorCodes.AQUA}premium{McColorCodes.DARK_GREEN}{McColorCodes.BOLD}+{McColorCodes.GRAY}";
            if (socket.SessionInfo.McName != "Ekwav" && socket.LatestSettings.Tier != hypixel.AccountTier.PREMIUM_PLUS)
            {
                socket.SendMessage(COFLNET + $"This is a {premPlus} setting. You are not a {premPlus} user :/ ");
                return;
            }
            await socket.UpdateSettings(settings =>
            {
                settings.Settings.FastMode = true;
                return settings;
            });
            hypixel.FlipperService.Instance.AddConnectionPlus(socket, false);
            socket.SendMessage(COFLNET + $"You enabled the fast mode, some settings don't take affect anymore");
        }
    }
}