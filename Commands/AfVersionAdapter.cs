using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class AfVersionAdapter : ModVersionAdapter
    {
        public AfVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }
        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var name = flip.Auction.Context["cname"];
            if(flip.Auction.Count > 1)
                name = $"{McColorCodes.GRAY}{flip.Auction.Count}x {name}";
            socket.Send(Response.Create("flip", new
            {
                id = flip.Auction.Uuid,
                startingBid = flip.Auction.StartingBid,
                itemName = name
            }));

            return true;
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }
    }
}
