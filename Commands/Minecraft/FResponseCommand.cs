using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;
public class FResponseCommand : McCommand
{
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        var parts = arguments.Trim('"').Split(' ');
        var uuid = parts[0];
        socket.ReceivedConfirm.TryRemove(uuid, out var value);
        System.Console.WriteLine($"Received confirm for {uuid} from {socket.SessionInfo.McName}");
        if ((socket.AccountInfo?.Tier ?? 0) >= Shared.AccountTier.SUPER_PREMIUM)
        {
            // make sure receival is published
            socket.GetService<PreApiService>().PublishReceive(uuid);
        }
        var worth = parts[1];
        return Task.CompletedTask;
    }
}