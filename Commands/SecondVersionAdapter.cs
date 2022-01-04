using System;
using System.Collections.Generic;
using hypixel;
using System.Threading.Tasks;
using Coflnet.Sky;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Commands.MC
{
    public class SecondVersionAdapter : ModVersionAdapter
    {
        public SecondVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            List<ChatPart> parts = await GetMessageparts(flip);

            SendMessage(parts.ToArray());

            if (socket.Settings?.ModSettings?.PlaySoundOnFlip ?? false && flip.Profit > 1_000_000)
                SendSound("note.pling", (float)(1 / (Math.Sqrt((float)flip.Profit / 1_000_000) + 1)));
            return true;
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