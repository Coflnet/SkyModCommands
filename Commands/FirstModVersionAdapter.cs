using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class FirstModVersionAdapter : IModVersionAdapter
    {
        MinecraftSocket socket;

        public FirstModVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
            SendUpdateMessage();
        }

        private void SendUpdateMessage()
        {
            socket.SendMessage(MinecraftSocket.COFLNET + McColorCodes.RED + "There is a newer mod version. click this to open discord and download it", "https://discord.com/channels/267680588666896385/890682907889373257/898974585318416395");
        }

        public Task<bool> SendFlip(FlipInstance flip)
        {
            socket.SendMessage(socket.GetFlipMsg(flip), "/viewauction " + flip.Auction.Uuid, "UPDATE");
            SendUpdateMessage();
            return Task.FromResult(true);
        }

        public void SendMessage(params ChatPart[] parts)
        {
            var part = parts.FirstOrDefault();
            socket.SendMessage(part.text, part.onClick, part.hover);
            SendUpdateMessage();
        }

        public void SendSound(string name, float pitch = 1f)
        {
            // no support
            SendUpdateMessage();
        }
    }
}