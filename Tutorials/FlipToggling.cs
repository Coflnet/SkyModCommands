using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class FlipToggling : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}You can toggle flips on or off with {McColorCodes.AQUA}/cofl flip\n"
                    + $"They are currently off, you can click this to execute", "/cofl flip",
                $"{McColorCodes.YELLOW}You can enable flipping autostart by using the command\n"
                + $"{McColorCodes.AQUA}/cofl flip always\n"
                + $"Alternative toggle with {McColorCodes.AQUA}/cofl set fas{McColorCodes.GRAY} (FlipperAutoStarrt)");
    }
}