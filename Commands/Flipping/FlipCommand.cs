using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Toggles flipping on or off
/// </summary>
public class FlipCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.Settings.DisableFlips = !socket.Settings.DisableFlips;
        var state = McColorCodes.DARK_GREEN + "ON";
        if(socket.Settings.DisableFlips)
            state = McColorCodes.RED + "OFF";
        socket.Dialog(db => db.Msg("Toggled flips " + state));
        await socket.sessionLifesycle.AccountInfo.Update();
    }
}