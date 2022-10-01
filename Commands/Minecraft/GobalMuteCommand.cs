using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC
{
    public class GobalMuteCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = arguments.Trim('"');
            var parts = args.Split(' ');
            var uuid = parts.Last();
            var mcName = parts.Skip(1).First();
            var isModerator = socket.GetService<ModeratorService>().IsModerator(socket);
            if (!isModerator)
                throw new CoflnetException("forbiden", "Whops, you don't seem to be a moderator. Therefore you can't mute other users");

            await socket.GetService<ChatService>().Mute(new()
            {
                Message = $"Violating rule {args[0]} with {args.Replace(uuid, "").Substring(2)}",
                Muter = socket.SessionInfo.McUuid,
                Reason = args,
                Uuid = uuid
            });
            socket.Dialog(db => db.Msg(McColorCodes.AQUA + mcName).MsgLine("was muted").AsGray());
        }
    }
}