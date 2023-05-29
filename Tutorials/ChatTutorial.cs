using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class ChatTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        var link = "https://www.youtube.com/watch?v=qAlHiCmQG4c&list=PLDpPmxIcq9tAssQlyJMBlSmSg5JOpq699&index=5&pp=iAQB";
        builder.MsgLine($"{McColorCodes.YELLOW}You can write into the chat with {McColorCodes.AQUA}/fc {{message}}.", link, "Click to watch tutorial video")
            .MsgLine($"{McColorCodes.YELLOW}Use {McColorCodes.AQUA}/fc{McColorCodes.YELLOW} (without message) to toggle seeing it.", link, "Click to watch tutorial video");
    }
}
