using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UpdatePurseCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.Settings.MaxCost = int.Parse(arguments.Trim('"'));
            await socket.sessionLifesycle.FlipSettings.Update(socket.Settings);
        }
    }
}