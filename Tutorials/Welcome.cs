using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class Welcome : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"Hello {McColorCodes.AQUA}{socket.SessionInfo.McName}")
            .MsgLine($"It looks like this is the first time using the §1C§6oflmod§f")
            .MsgLine($"{McColorCodes.YELLOW}The mod starts automatically when you join skyblock. \n{McColorCodes.ITALIC} More info in the hover effect of this message", null,
                $"{McColorCodes.YELLOW}You can disable autostart by using the command\n"
                + $"{McColorCodes.AQUA}/cofl set privacyAutoStart false\n"
                + $"There are explanations in the hove effect of most messages");
    }
}
