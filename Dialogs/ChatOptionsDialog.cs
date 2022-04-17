using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class ChatOptionsDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            var userName = context.Context.Split(' ')[0];
            return New()
                .Msg("What do you want to do?")
                .LineBreak()
                .CoflCommand<MuteCommand>("  Mute " + userName, userName, McColorCodes.GRAY + "I don't want to get anymore messages from this user").LineBreak()
                .DialogLink<ChatReportDialog>("  Report this message ", context.Context, McColorCodes.GRAY + "report message to moderator").LineBreak()
                .BlockCommands()
            ;
        }
    }
}
