using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class ReferencesWrongDialog : Dialog
    {
        public override ChatPart[] GetResponse(string context)
        {
            return New()
                .Msg("Please elaborate in more detail what is wrong")
                .Break()
                .CoflCommand<ReportCommand>("* enchants are not matching", "enchants not matching", "Report enchant not matching").Break()
                .CoflCommand<ReportCommand>("* the references are outdated ", "outdated references", "Report outdated references").Break()
                .DialogLink<EchoDialog>(
                    "* other properties not matching", 
                    $"Please use {McColorCodes.AQUA}/cofl report {{message}} {McColorCodes.GRAY}to explain what is not matching", 
                    "Something else it not matching")

            ;
        }
    }
}
