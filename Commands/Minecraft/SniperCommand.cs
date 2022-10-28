using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class FastCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (socket.SessionInfo.McName != "Ekwav" && socket.sessionLifesycle.AccountInfo.Value.Tier != AccountTier.PREMIUM_PLUS)
            {
                socket.SendMessage(COFLNET + $"This setting is currently in development. You can't use it yet. :/ ");
                return;
            }
            FlipperService.Instance.AddConnectionPlus(socket, false);
            socket.SendMessage(COFLNET + $"You enabled the fast mode, some settings don't take affect anymore");
        }
    }
}