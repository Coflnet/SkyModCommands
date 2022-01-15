using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class OverpricedDialog : Dialog
    {
        public override ChatPart[] GetResponse(string context)
        {
            return New()
                .Msg("Please take a look at the reference Auctions")
                .Break()
                .DialogLink<ReferencesWrongDialog>("* The most expensive references are not similar to the flip", context, "I think references are wrong").Break()
                .CoflCommand<ReportCommand>("* The price changed during a recent mayor", "mayor changed", "This item changes value based on mayor").Break()
                .CoflCommand<ReportCommand>("* This item is being manipulated", "being manipulated", "I think this item is being manipulated").Break()
                .CoflCommand<ReportCommand>("* The value of this item dropped due to an update", "outdated references", "Report outdated references").Break()
                .DialogLink<EchoDialog>("* I checked its actually correct", "Thanks for your feedback, have a nice day", "Everything okay")

            ;
        }
    }
}
