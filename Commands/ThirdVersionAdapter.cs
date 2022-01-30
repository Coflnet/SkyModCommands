using System;
using System.Linq;
using System.Threading.Tasks;
using hypixel;

namespace Coflnet.Sky.Commands.MC
{
    public class ThirdVersionAdapter : ModVersionAdapter
    {


        public ThirdVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var uuid = flip.Auction.Uuid;

            socket.Send(Response.Create("flip", new
            {
                messages = await GetMessageparts(flip),
                id = uuid,
                worth = flip.Profit,
                cost = flip.Auction.StartingBid,
                sound = (string)null
            }));

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