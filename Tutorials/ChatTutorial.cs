using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class ChatTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}You can write into the chat with {McColorCodes.AQUA}/fc {{message}}.")
            .MsgLine($"{McColorCodes.YELLOW}Use {McColorCodes.AQUA}/fc{McColorCodes.YELLOW} (without message) to toggle seeing it.");
    }
}
