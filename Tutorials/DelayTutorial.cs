using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class DelayTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        var link = "https://www.youtube.com/watch?v=XdOf1KuYEzA&list=PLDpPmxIcq9tAssQlyJMBlSmSg5JOpq699&index=10&pp=iAQB";
        builder.MsgLine($"{McColorCodes.YELLOW}You may receive flips with a bit of delay due to various reasons", 
            link, 
            "Click to watch tutorial video\n"
            + $"{McColorCodes.YELLOW}The most common is a regular split second delay after you bought a flip quickly\n"
            + $"{McColorCodes.YELLOW}This is added to balance flips for all users to give you and everyone else\n"
            + $"{McColorCodes.YELLOW}a fair chance to buy a similar amount of flips.");
    }
}
