using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class OverpricedDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            return New()
                .Msg("Please take a look at the reference Auctions")
                .LineBreak()
                .DialogLink<ReferencesWrongDialog>("* The most expensive references are not similar to the flip", context.Context, "I think references are wrong").LineBreak()
                .CoflCommand<ReportCommand>("* The price changed during a recent mayor", "mayor changed", "This item changes value based on mayor").LineBreak()
                .CoflCommand<ReportCommand>("* This item is being manipulated", "being manipulated", "I think this item is being manipulated").LineBreak()
                .CoflCommand<ReportCommand>("* The value of this item dropped due to an update", "outdated references", "Report outdated references").LineBreak()
                .DialogLink<EchoDialog>("* I checked its actually correct", "Thanks for your feedback, have a nice day", "Everything okay")

            ;
        }
    }
}
