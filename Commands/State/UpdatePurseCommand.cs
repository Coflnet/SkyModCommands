using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UpdatePurseCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if(socket.Settings == null || socket.Settings.ModSettings.NoAdjustToPurse)
                return;
            socket.Settings.MaxCost = int.Parse(arguments.Trim('"'));
            socket.Settings.LastChanged = "preventUpdateMsg";
            await socket.sessionLifesycle.FlipSettings.Update(socket.Settings);
        }
    }
}