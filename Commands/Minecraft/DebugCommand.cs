using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class DebugSCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var privacySettings = socket.sessionLifesycle.PrivacySettings.Value;
            if(privacySettings.ChatRegex == ".*")
            {
                privacySettings.ChatRegex = InventoryModSession.DefaultChatRegex;
                await socket.sessionLifesycle.PrivacySettings.Update();
                socket.SendMessage(COFLNET + $"Debug disabled, please execute /cofl debug to enable");
                return;
            }
            socket.SendMessage(COFLNET + $"Debug enabled, if you didn't do this intentionally, please execute /cofl debug again to disable");
            privacySettings.ChatRegex = ".*";
            await socket.sessionLifesycle.PrivacySettings.Update();
            System.Console.WriteLine("debug command");
        }
    }

    public class FResponseCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var parts = arguments.Trim('"').Split(' ');
            var uuid = parts[0];
            socket.ReceivedConfirm.TryRemove(uuid, out var value);
            System.Console.WriteLine($"Received confirm for {uuid} from {socket.SessionInfo.McName}");
            var worth = parts[1];
            return Task.CompletedTask;
        }
    }
}