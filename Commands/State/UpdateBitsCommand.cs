using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UpdateBitsCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SessionInfo.Bits = long.Parse(arguments.Trim('"'));
            return Task.CompletedTask;
        }
    }
}