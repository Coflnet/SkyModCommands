using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class ChatTutorial : TutorialBase
{
    public const string VideoLink = "https://www.youtube.com/watch?v=qAlHiCmQG4c&list=PLDpPmxIcq9tAssQlyJMBlSmSg5JOpq699&index=5&pp=iAQB";

    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}You can write into the chat with {McColorCodes.AQUA}/fc {{message}}.", VideoLink, "Click to watch tutorial video")
            .MsgLine($"{McColorCodes.YELLOW}Use {McColorCodes.AQUA}/fc{McColorCodes.YELLOW} (without message) to toggle seeing it.", VideoLink, "Click to watch tutorial video");
    }
}

public class WhitelistTutorial : TutorialBase
{
    public const string VideoLink = "https://youtu.be/k4eZ3_hroT4?si=A8YCdUyIzj5HnkgX&t=232";

    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine("Whitelisted items skip your other settings.",
            VideoLink,
            $"{McColorCodes.YELLOW}Click the Text {McColorCodes.WHITE}Whitelisted \n"
            + $"{McColorCodes.YELLOW}to see the filter that caused the flip to show up.\n"
            + $"{McColorCodes.YELLOW}You can whitelist items with \n{McColorCodes.AQUA}/cofl whitelist add {{Item Name}}.\n"
            + "Click to watch tutorial video");
    }
}