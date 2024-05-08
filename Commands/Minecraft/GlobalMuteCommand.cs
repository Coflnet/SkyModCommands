using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC
{
    public class GlobalMuteCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = arguments.Trim('"');
            var parts = args.Split(' ');
            var uuid = parts.Last();
            var mcName = parts.Skip(1).First();
            var isModerator = socket.GetService<ModeratorService>().IsModerator(socket);
            if (!isModerator)
                throw new CoflnetException("forbidden", "Whoops, you don't seem to be a moderator. Therefore you can't mute other users");

            // verify format
            if (!int.TryParse(args[0].ToString(), out var rule) || !Guid.TryParse(uuid, out var guid))
                throw new CoflnetException("invalid_format", "The format is <rule id> <username> <any word> <reason> <uuid>");

            socket.Dialog(db => db.MsgLine("Muting ").AsGray().Msg(McColorCodes.AQUA + mcName));
            await socket.GetService<ChatService>().Mute(new()
            {
                Message = $"Violating rule {rule} with \"{args.Replace(" " + uuid, "").Substring(args.IndexOf(parts.Skip(3).First()))}\"",
                Muter = socket.SessionInfo.McUuid,
                Reason = args,
                Uuid = guid.ToString("N")
            });
            socket.Dialog(db => db.Msg(McColorCodes.AQUA + mcName).MsgLine(" was muted successfully").AsGray());
        }
    }
}