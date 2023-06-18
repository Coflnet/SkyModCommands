using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Tutorials;
namespace Coflnet.Sky.Commands.MC;


/// <summary>
/// Toggles flipping on or off
/// </summary>
[CommandDescription("Toggles flipping on or off", "Usage: /cl flip <never|always>")]
public class FlipCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var flipSettings = socket.sessionLifesycle.FlipSettings;
        switch (arguments.Trim('"'))
        {
            case "never":
                flipSettings.Value.DisableFlips = true;
                await flipSettings.Update();
                break;
            case "always":
                flipSettings.Value.ModSettings.AutoStartFlipper = true;
                flipSettings.Value.DisableFlips = false;
                await flipSettings.Update();
                socket.SessionInfo.FlipsEnabled = true;
                WriteCurrentState(socket);
                socket.Dialog(db => db.CoflCommand<SetCommand>(
                    $"To disable flipper autostart do \n{McColorCodes.AQUA}/cofl set modAutoStartFlipper false",
                    "modAutoStartFlipper false",
                    $"Click to turn autostart off, short hand to toggle is {McColorCodes.AQUA}/cl s fas"));
                break;
            default:
                socket.SessionInfo.FlipsEnabled = !socket.SessionInfo.FlipsEnabled;
                WriteCurrentState(socket);
                socket.sessionLifesycle.UpdateConnectionTier(socket.AccountInfo);
                break;
        }
        await socket.TriggerTutorial<Flipping>();

    }

    private static void WriteCurrentState(MinecraftSocket socket)
    {
        var state = McColorCodes.DARK_GREEN + "ON";
        if (!socket.SessionInfo.FlipsEnabled)
            state = McColorCodes.RED + "OFF";
        socket.Dialog(db => db.CoflCommand<FlipCommand>("Toggled flips " + state, "", $"Toggle them again\nexecutes {McColorCodes.AQUA}/cofl flip"));
    }
}
