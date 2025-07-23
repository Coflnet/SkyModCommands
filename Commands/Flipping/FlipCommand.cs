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
                flipSettings.Value.ModSettings.AhDataOnlyMode = true;
                flipSettings.Value.ModSettings.AutoStartFlipper = false;
                flipSettings.Value.LastChanged = "flip disabled";
                await flipSettings.Update();
                socket.SessionInfo.FlipsEnabled = false;
                break;
            case "always":
                flipSettings.Value.ModSettings.AutoStartFlipper = true;
                flipSettings.Value.DisableFlips = false;
                flipSettings.Value.ModSettings.AhDataOnlyMode = false;
                flipSettings.Value.LastChanged = "flips enabled";
                await flipSettings.Update();
                socket.SessionInfo.FlipsEnabled = true;
                WriteCurrentState(socket);
                socket.Dialog(db => db.CoflCommand<SetCommand>(
                    $"To disable flipper autostart do \n{McColorCodes.AQUA}/cofl set modAutoStartFlipper false",
                    "modAutoStartFlipper false",
                    $"Click to turn autostart off, short hand to toggle is {McColorCodes.AQUA}/cl s fas"));
                break;
            case "off":
            case "disable":
            case "false":
                socket.SessionInfo.FlipsEnabled = false;
                WriteCurrentState(socket);
                break;
            default:
                socket.SessionInfo.FlipsEnabled = !socket.SessionInfo.FlipsEnabled;
                flipSettings.Value.ModSettings.AhDataOnlyMode = false;
                WriteCurrentState(socket);
                socket.sessionLifesycle.UpdateConnectionTier(await socket.sessionLifesycle.TierManager.GetCurrentCached());
                break;
        }
        await socket.TriggerTutorial<Flipping>();

    }

    private static void WriteCurrentState(MinecraftSocket socket)
    {
        var state = McColorCodes.DARK_GREEN + "ON";
        if (!socket.SessionInfo.FlipsEnabled)
            state = McColorCodes.RED + "OFF" + $"\n {McColorCodes.GRAY}(use {McColorCodes.ITALIC}/cl flip never{McColorCodes.RESET}{McColorCodes.GRAY} to disable permanently)";
        socket.Dialog(db => db.CoflCommand<FlipCommand>("Toggled flips " + state, "", $"Toggle them again\nexecutes {McColorCodes.AQUA}/cofl flip"));
        if (socket.ModAdapter is AfVersionAdapter)
            socket.Dialog(db => db.MsgLine("You are using an auto flipper client and thus have flips always enabled. Stop your client to stop receiving flips"));
    }
}
