using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Command doing absolutely nothing to use as placeholder
    /// </summary>
    public class VoidCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
        }
    }
}