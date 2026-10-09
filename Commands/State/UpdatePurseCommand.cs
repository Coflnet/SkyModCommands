using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UpdatePurseCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            if (double.TryParse(arguments.Trim('"'), out var newVal))
                socket.SessionInfo.Purse = (long)newVal;
            return Task.CompletedTask;
        }
    }
}
