using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UpdateBitsCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            // does nothing for now
            return Task.CompletedTask;
        }
    }
}