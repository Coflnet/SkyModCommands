using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class FirstModVersionAdapter : IModVersionAdapter
    {
        private const string LinkToRelease = "https://github.com/Coflnet/skyblockmod/releases";
        private MinecraftSocket socket;

        public FirstModVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
            SendUpdateMessage();
        }

        private void SendUpdateMessage()
        {
            socket.SendMessage(MinecraftSocket.COFLNET + McColorCodes.RED + "There is a newer mod version. Click this to open it on github", LinkToRelease);
        }

        public Task<bool> SendFlip(FlipInstance flip)
        {
            socket.SendMessage(socket.GetFlipMsg(flip), LinkToRelease, "UPDATE");
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

        public void SendLoginPrompt(string v)
        {
            SendUpdateMessage();
        }

        public void OnAuthorize(AccountInfo accountInfo)
        {
            
        }
    }
}