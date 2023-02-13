using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class MuteCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var name = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(arguments);
            var settings = socket.sessionLifesycle.AccountSettings;
            if (settings == null)
            {
                socket.SendMessage($"You have to be logged in to do this", null, "mutes are stored on your account");
                return;
            }
            var val = settings.Value;
            if (val == null)
                val = new();


            if (string.IsNullOrEmpty(name))
            {
                DisplayMuted(socket, val);
                return;
            }
            var uuid = await socket.GetPlayerUuid(name);
            if (uuid == socket.SessionInfo.McUuid)
            {
                socket.SendMessage(COFLNET + $"You can't mute yourself");
                return;
            }

            if (val.MutedUsers == null)
                val.MutedUsers = new System.Collections.Generic.HashSet<UserMute>();
            val.MutedUsers.Add(new UserMute(uuid, name));
            await settings.Update(val);

            socket.SendMessage(COFLNET + $"You muted {name}");
        }

        private static void DisplayMuted(MinecraftSocket socket, AccountSettings val)
        {
            if (val == null || val.MutedUsers.Count == 0)
            {
                socket.SendMessage($"You don't have anyone muted, use {McColorCodes.AQUA}/cofl mute NAME{DEFAULT_COLOR} to mute someone");
                return;
            }

            var mutedList = val.MutedUsers.Select(u => new ChatPart($"\n{McColorCodes.DARK_GRAY}> {McColorCodes.WHITE}{u.OrigianlName}"));
            // wants to list it 
            socket.SendMessage(DialogBuilder.New.Msg("These users are muted by you:")
                .ForEach(val.MutedUsers, (db, u) => db.Break.CoflCommand<UnMuteCommand>($"\n{McColorCodes.DARK_GRAY}> {McColorCodes.WHITE}{u.OrigianlName} {McColorCodes.YELLOW}[UNMUTE]", u.OrigianlName, "unmute " + McColorCodes.AQUA + u.OrigianlName)));
            return;
        }
    }
}