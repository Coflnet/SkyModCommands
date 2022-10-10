using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class DebugSCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendMessage(COFLNET + $"Debug enabled, if you didn't do this intentionally, please execute /cofl start to disable");
            socket.sessionLifesycle.PrivacySettings.Value.ChatRegex = ".*";
            await socket.sessionLifesycle.PrivacySettings.Update();
            System.Console.WriteLine("debug command");
        }
    }
}