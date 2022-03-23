using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class SlowSellDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            return New()
                .Msg("You can filter slow selling flips by increasing your min volume filter.").LineBreak()
                .Msg("The volume is the average amount of sales in the last 24 hours.").LineBreak()
                .Msg("Ie. 50 means that it takes half an hour to sell on average.")
            ;
        }
    }
}
