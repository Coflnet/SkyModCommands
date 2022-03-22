using System;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class HelpCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.ExecuteCommand("/cofl");
            return Task.CompletedTask;
        }
    }
}