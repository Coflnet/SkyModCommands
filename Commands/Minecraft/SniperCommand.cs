using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class FastCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (socket.SessionInfo.McName != "Ekwav" && socket.sessionLifesycle.AccountInfo.Value.Tier != hypixel.AccountTier.PREMIUM_PLUS)
            {
                socket.SendMessage(COFLNET + $"This setting is currently in development. You can't use it yet. :/ ");
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