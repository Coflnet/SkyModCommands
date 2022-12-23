using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class ChatReportDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            return New()
                .Msg("Are you sure you want to report this message")
                .LineBreak()
                .CoflCommand<ReportCommand>(McColorCodes.RED + " YES ", "Chat report for " + context.Context, "Confirm report").LineBreak()
                .DialogLink<EchoDialog>(McColorCodes.GREEN + " No, I actually don't ", "Okay! have fun chatting :)", "cancel the report").LineBreak()
                .BlockCommands()
            ;
        }
    }
}
