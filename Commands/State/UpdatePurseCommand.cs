using System;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UpdatePurseCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var newVal = double.Parse(arguments.Trim('"'));
            socket.SessionInfo.Purse = (long)newVal;
        }
    }
}