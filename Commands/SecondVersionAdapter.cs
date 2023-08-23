using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class SecondVersionAdapter : ModVersionAdapter
    {
        public SecondVersionAdapter(MinecraftSocket socket) : base(socket)
        {
            SendOutDated();
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            List<ChatPart> parts = await GetMessageparts(flip);

            SendMessage(parts.ToArray());
            SendOutDated();
            return true;
        }

        private void SendOutDated()
        {
            SendMessage(new DialogBuilder().MsgLine("There is a newer mod version available. Please update as soon as possible. \nYou can click this to be redirected to the download.",
                                        "https://github.com/Coflnet/skyblockmod/releases",
                                        "opens github"));
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }

        public override void SendSound(string name, float pitch = 1)
        {
            socket.Send(Response.Create("playSound", new { name, pitch }));
        }
    }
}