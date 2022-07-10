using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class DebugCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendMessage(COFLNET + $"Debug enabled, if you didn't do this intentionally, please execute /cofl start to disable");
            socket.sessionLifesycle.PrivacySettings.Value.ChatRegex = ".*";
            return socket.sessionLifesycle.PrivacySettings.Update();
        }
    }
}