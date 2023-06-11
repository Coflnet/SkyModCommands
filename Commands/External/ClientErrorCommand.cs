using System.Diagnostics;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Called by the client when an error occurs
/// </summary>
public class ClientErrorCommand : McCommand
{
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        // currently every command param is already traced separately
        // so we don't need to do anything here except tag it
        Activity.Current?.AddTag("mcname", socket.SessionInfo.McName);
        Activity.Current?.AddTag("mcuuid", socket.SessionInfo.McUuid);
        Activity.Current?.AddTag("userid", socket.UserId);
        return Task.Delay(arguments.Length * 6);
    }
}