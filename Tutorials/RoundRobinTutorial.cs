using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class RoundRobinTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}Flips with a red dot behind the item name ({McColorCodes.RED}.{McColorCodes.YELLOW}) are RR (Round Robin flips)\n"
             + $"{McColorCodes.YELLOW}You have {McColorCodes.AQUA}3 full seconds{McColorCodes.YELLOW} to buy them before they are sent to another user.", null,
                $"{McColorCodes.YELLOW}Green dots mark a flip as the Round Robin of somebody\n else which didn't pass their filters.\n"
                + "Because you are using pre-api you get them before other users that aren't on pre-api as wel");
    }
}
