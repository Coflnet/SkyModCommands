using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class AutotipTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}Mod users tip each other", "/tipall",
            "If you don't want to tip other users"
            + "and earn exp and coins while doing so."
            + $"Use {McColorCodes.AQUA}/cofl autotip disable{McColorCodes.GRAY} to turn it off.");
    }
}
