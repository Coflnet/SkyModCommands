using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class AdventCalendarTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.CoflCommand<PurchaseCommand>("The advent calendar is made out of 25 simple questions that you get a reward for answering correctly.",
            "advent-calendar",
            $"By default you get 5 CoflCoins for a correct answer"
            + "\nYou can upgrade that to get 50 CoflCoins instead."
            + "\nClick here to upgrade");
    }
}