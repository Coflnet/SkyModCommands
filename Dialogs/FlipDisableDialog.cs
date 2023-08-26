using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs;
public class FlipDisableDialog : Dialog
{
    public override ChatPart[] GetResponse(DialogArgs context)
    {
        return New()
        .MsgLine("Alright, not gonna put flips in chat. Enjoy access to our huge dataset.",
            hover: "Please report invalid prices on our discord\nif you find any")
        .CoflCommand<FlipCommand>(McColorCodes.BLUE + " disable this question ",
            "never",
            $"I don't want to flip\nto reenable use {McColorCodes.AQUA}/cofl flip always");
    }
}
