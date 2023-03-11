using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class BinGuiVersionAdapter : ModVersionAdapter
    {
        public BinGuiVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var uuid = flip.Auction.Uuid;
            long worth = GetWorth(flip);

            socket.Send(Response.Create("flip", new
            {
                messages = await GetMessageparts(flip),
                id = uuid,
                worth = worth,
                sound = new { name = (string)(string)(socket.Settings?.ModSettings?.PlaySoundOnFlip ?? false && flip.Profit > 1_000_000 ? "note.pling" : null), pitch = 1 },
                auction = flip.Auction,
                render = Random.Shared.Next(3) switch
                {
                    1 => "21d837ca222cbc0bc12426f5da018c3a931b406008800960a9df112a596e7d62",
                    2 => "sea_lantern",
                    _ => "leather_leggings"
                }
            }));
            if (DateTime.UtcNow.Month == 4 && DateTime.UtcNow.Day == 1 && Random.Shared.Next(200) == 1)
            {
                await SendAprilFools();
            }

            if (flip.Profit > 2_000_000)
            {
                socket.ExecuteCommand($"/cofl fresponse {uuid} {flip.Auction.StartingBid}");
            }
            return true;
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }
    }
}
