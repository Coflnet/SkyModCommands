using System.Linq;
using System.Threading.Tasks;
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

            await socket.GetService<ChatService>().Mute(new()
            {
                Message = $"Violating rule {args[0]} with {args.Replace(uuid, "").Substring(2)}",
                Muter = socket.SessionInfo.McUuid,
                Reason = args,
                Uuid = uuid
            });
        }
    }
}