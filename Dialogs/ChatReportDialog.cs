using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class ChatReportDialog : Dialog
    {
        public override ChatPart[] GetResponse(string context)
        {
            return New()
                .Msg("Are you sure you want to report this message")
                .Break()
                .CoflCommand<ReportCommand>(McColorCodes.RED + " YES ", "Chat report for " + context, "Confirm report").Break()
                .DialogLink<EchoDialog>(McColorCodes.GREEN + " No, I actually don't ", "Okay! have fun chatting :)", "cancle the report").Break()
            ;
        }
    }
}
