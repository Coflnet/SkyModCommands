using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class TimeCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendMessage(COFLNET + JsonConvert.SerializeObject(socket.LastSent.LastOrDefault()?.Auction.Context));
        }
    }
}