using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class ChatRulesTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}The chat rules are:")
            .MsgLine($" 1) {McColorCodes.AQUA}Be Nice", null, $"{McColorCodes.YELLOW}Just be nice to each other")
            .MsgLine($" 2) {McColorCodes.AQUA}Don't advertise something nobody asked for", null, $"{McColorCodes.YELLOW}This includes priming someone to ask something");
    }
}
