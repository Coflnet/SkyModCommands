using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class Flipping : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}The flipping system uses a complex comparison system to determine how much an item is worth.", 
                "https://www.youtube.com/watch?v=nfMo5CeJDgc&list=PLDpPmxIcq9tAssQlyJMBlSmSg5JOpq699&index=9&pp=iAQB", "Click to watch explanation video")
            .MsgLine($"{McColorCodes.AQUA}If you think the comparison missed something and/or estimated the value incorrectly, please report the flip via the ✥", 
                    "https://discord.com/channels/267680588666896385/884002032392998942", 
                $"{McColorCodes.YELLOW}Reporting is done via the flip menu (thats the {McColorCodes.WHITE}✥{McColorCodes.YELLOW} at the end of every flip)\n"
                + $"{McColorCodes.YELLOW}A report makes sure all available information is saved for investigation.\n"
                + $"{McColorCodes.YELLOW}To make sure its not lost please create a bug report thread on our discord\n"
                + $"{McColorCodes.YELLOW}with the 6 digit code you get after making one. (that helps to find it)");
    }
}