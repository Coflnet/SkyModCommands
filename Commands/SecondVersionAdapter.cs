using System;
using System.Collections.Generic;
using Coflnet.Sky.Core;
using System.Threading.Tasks;
using Coflnet.Sky;
using Microsoft.Extensions.DependencyInjection;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class SecondVersionAdapter : ModVersionAdapter
    {
        public SecondVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
            SendOutDated();
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            List<ChatPart> parts = await GetMessageparts(flip);

            SendMessage(parts.ToArray());
            return true;
        }

        private void SendOutDated()
        {
            SendMessage(new DialogBuilder().MsgLine("There is a newer mod version available. Please update as soon as possible. \nYou can click this to be redirected to the download.",
                                        "https://discord.com/channels/267680588666896385/890682907889373257/955963133070032986",
                                        "opens discord"));
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