using System;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;
public class ConnectCommand : McCommand
{
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.SendMessage("This client program does not implement the connect command correctly.");
        socket.AccountInfo.LastMacroConnect = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
