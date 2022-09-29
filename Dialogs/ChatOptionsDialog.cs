using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class ChatOptionsDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            var userName = context.Context.Split(' ')[0];
            var isModerator = context.socket.GetService<ModeratorService>().IsModerator(context.socket);
            return New()
                .Msg("What do you want to do?")
                .LineBreak()
                .CoflCommand<MuteCommand>("  Mute " + userName, userName, McColorCodes.GRAY + "I don't want to get anymore messages from this user").LineBreak()
                .If(() => !isModerator, db => db.DialogLink<ChatReportDialog>("  Report this message ", context.Context, McColorCodes.GRAY + "report message to moderator"))
                .If(() => isModerator, db => db.CoflCommand<GobalMuteCommand>("Global chat mute the user for rule 1", "1 " + context.Context, $"x10 mute time for rule 1")).LineBreak()
                .If(() => isModerator, db => db.CoflCommand<GobalMuteCommand>("Global chat mute the user for rule 2", "2 " + context.Context, $"x3 mute time"))
            ;
        }
    }
}
