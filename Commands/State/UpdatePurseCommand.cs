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
            if (socket.Settings == null || socket.Settings.ModSettings.NoAdjustToPurse)
                return;
            if (Math.Abs(newVal - socket.Settings.MaxCost) < 50)
                return; // minimal change not relevant (reduce load on db updates)
            socket.Settings.MaxCost = (long)newVal;
        }
    }
}