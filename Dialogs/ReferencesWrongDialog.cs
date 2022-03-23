using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class ReferencesWrongDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            return New()
                .Msg("Please elaborate in more detail what is wrong")
                .LineBreak()
                .CoflCommand<ReportCommand>("* enchants are not matching", "enchants not matching", "Report enchant not matching").LineBreak()
                .CoflCommand<ReportCommand>("* the references are outdated ", "outdated references", "Report outdated references").LineBreak()
                .DialogLink<EchoDialog>(
                    "* other properties not matching", 
                    $"Please use {McColorCodes.AQUA}/cofl report {{message}} {McColorCodes.GRAY}to explain what is not matching", 
                    "Something else it not matching")

            ;
        }
    }
}
