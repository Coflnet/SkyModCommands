using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC
{
    public class ThirdVersionAdapter : ModVersionAdapter
    {
        private Random rng = new Random();

        public ThirdVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var uuid = flip.Auction.Uuid;
            if(socket.GetService<PreApiService>().IsSold(uuid))
            {
                var parts = await GetMessageparts(flip);
                parts.Insert(0,new ChatPart(McColorCodes.RED + "[SOLD]", "/viewauction " + uuid, "This auction has likely already been sold"));
                SendMessage(parts.ToArray());
                return true;
            }
            long worth = GetWorth(flip);

            socket.Send(Response.Create("flip", new
            {
                messages = await GetMessageparts(flip),
                id = uuid,
                worth = worth,
                cost = flip.Auction.StartingBid,
                sound = (string)"note.pling"
            }));
            if (DateTime.UtcNow.Month == 4 && DateTime.UtcNow.Day == 1 && rng.Next(200) == 1)
            {
                await SendAprilFools();
            }

            if (socket.Settings?.ModSettings?.PlaySoundOnFlip ?? false && flip.Profit > 1_000_000)
                SendSound("note.pling", (float)(1 / (Math.Sqrt((float)flip.Profit / 1_000_000) + 1)));
            return true;
        }


        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }


    }

    public class InventoryVersionAdapter : ThirdVersionAdapter
    {
        public InventoryVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }
    }
}