using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class Blocked : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}The blocked mesage tells you how many flips were blocked.\n"
            + $"{McColorCodes.YELLOW}You can click it to get the reason of why flips were blocked.", null,
                "To get the message less often you can use the command\n" + $"{McColorCodes.AQUA}/cofl set modblockedmsg 120\n"
                + $"{McColorCodes.YELLOW} to get it every 2 hours instead of every minute."
                + $"\n{McColorCodes.YELLOW}You can also use {McColorCodes.AQUA}/cofl blocked {{search}}\n"
                + $"{McColorCodes.YELLOW}to search recent blocked flips for a reason and maybe adjust your settings.");
    }
}