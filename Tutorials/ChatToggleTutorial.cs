using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class ChatToggleTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}With {McColorCodes.AQUA}/fc{McColorCodes.YELLOW} you can toggle seeing the chat.", ChatTutorial.VideoLink, "Click to watch tutorial video")
            .MsgLine($"{McColorCodes.GREEN}You can toggle all your messages being sent into the CoflChat with {McColorCodes.AQUA}/fc toggle", "/fc toggle", "Execute it again to switch back\nclick to execute");
    }
}
