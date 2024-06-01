using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class ChatOptionsDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            var userName = context.Context.Split(' ')[0];
            if(userName == context.socket.SessionInfo.McName)
            {
                return New()
                    .Msg("There are no options for your own messages")
                    .LineBreak();
            }
            var isModerator = context.socket.GetService<ModeratorService>().IsModerator(context.socket);
            return New()
                .Msg("What do you want to do?")
                .LineBreak()
                .CoflCommand<MuteCommand>("  Mute " + userName, userName, McColorCodes.GRAY + "I don't want to get anymore messages from this user").LineBreak()
                .CoflCommand<GetMcNameForCommand>("  Get IGN", userName, McColorCodes.GRAY + "Get the IGN of this user\n(player might be nicked)").LineBreak()
                .If(() => !isModerator, db => db.DialogLink<ChatReportDialog>("  Report this message ", context.Context, McColorCodes.GRAY + "report message to moderator").LineBreak())
                .CoflCommand<ChatCommand>(" -> disable chat", "", McColorCodes.GRAY + "turn off showing this chat").LineBreak()
                .If(() => isModerator, db => db.CoflCommand<GlobalMuteCommand>("Global chat mute the user for rule 1", "1 " + context.Context, $"x10 mute time for rule 1")).LineBreak()
                .If(() => isModerator, db => db.CoflCommand<GlobalMuteCommand>("Global chat mute the user for rule 2", "2 " + context.Context, $"x3 mute time"))
            ;
        }
    }
}
