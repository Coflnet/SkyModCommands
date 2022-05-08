using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UpdatePurseCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.Settings.MaxCost = int.Parse(arguments.Trim('"'));
            return Task.CompletedTask;
        }
    }
}