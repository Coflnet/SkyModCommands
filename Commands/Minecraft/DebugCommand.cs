using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Model;

namespace Coflnet.Sky.Commands.MC
{
    public class DebugSCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SessionInfo.IsDebug = !socket.SessionInfo.IsDebug;
            var privacySettings = socket.sessionLifesycle.PrivacySettings.Value;
            if (privacySettings.ChatRegex == ".*")
            {
                privacySettings.ChatRegex = Shared.PrivacySettings.DefaultChatRegex;
                await socket.sessionLifesycle.PrivacySettings.Update();
                socket.SendMessage(COFLNET + $"Debug disabled, please execute /cofl debug to enable");
                return;
            }
            socket.SendMessage(COFLNET + $"Debug enabled, if you didn't do this intentionally, please execute /cofl debug again to disable");
            privacySettings.ChatRegex = ".*";
            await socket.sessionLifesycle.PrivacySettings.Update();
            foreach (var item in socket.Headers)
            {
                socket.Dialog(db => db.Msg($"{item}:{socket.Headers[item.ToString()]}"));
            }
            System.Console.WriteLine("debug command");
        }
    }
}