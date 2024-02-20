using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class CaptchaTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}Congrats, you flipped enough that we need to verify that you are not a bot."
            + $"Please make sure that the {McColorCodes.DARK_GREEN}green line |{McColorCodes.YELLOW} at the right lines up. If it doesn't click {McColorCodes.AQUA}Vertical\n",
            null, "By verifying that you are a human\nyou help us to keep the game fair and fun for everyone.\nThank you for your understanding");
    }
}