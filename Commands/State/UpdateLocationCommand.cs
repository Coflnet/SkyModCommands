using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Updates in what region on the server the player is
    /// </summary>
    public class UpdateLocationCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            // does nothing for now
            return Task.CompletedTask;
        }
    }
}