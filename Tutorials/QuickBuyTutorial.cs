using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class QuickBuyTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}You can enable a custom gui with {McColorCodes.AQUA}/cofl setgui cofl{McColorCodes.YELLOW} "
            + $"and use the {McColorCodes.AQUA}Open best/next flip{McColorCodes.YELLOW} keybind to open the next flip as soon as it arrives.\n"
            + $"{McColorCodes.GRAY}(its in the minecraft settings)");
    }
}
