using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Updates what hypixel game server the player is on
    /// </summary>
    public class UpdateServerCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            // does nothing for now
            return Task.CompletedTask;
        }
    }
}