using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UploadTabCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            // does nothing for now
            return Task.CompletedTask;
        }
    }
}