using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class PreApiTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}Welcome to pre-api\n"
            + $"{McColorCodes.YELLOW}For as long as its active you are first in line to receive new found flips before the hypixel api updates.\n"
            , null,
            $"{McColorCodes.YELLOW}You can use {McColorCodes.AQUA}/cofl preapi{McColorCodes.YELLOW} to see how much time you have left.\n"
        );
    }
}
