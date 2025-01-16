using System.Threading.Tasks;
using Newtonsoft.Json;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC
{
    public class BlockedImportCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var service = socket.GetService<IBlockedService>();
            var blocked = JsonConvert.DeserializeObject<MinecraftSocket.BlockedElement[]>(arguments);
            foreach (var item in blocked)
            {
                item.Reason += "(us)";
            }
            await service.ArchiveBlockedFlipsUntil(new(blocked), socket.UserId, 0);
        }
    }
}