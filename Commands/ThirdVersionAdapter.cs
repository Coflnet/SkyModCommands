using System.Linq;
using System.Threading.Tasks;
using hypixel;

namespace Coflnet.Sky.Commands.MC
{
    public class ThirdVersionAdapter : IModVersionAdapter
    {
        MinecraftSocket socket;

        public ThirdVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
        }

        public async Task<bool> SendFlip(FlipInstance flip)
        {
            var message = socket.GetFlipMsg(flip);
            var openCommand = "/viewauction " + flip.Auction.Uuid;
            var interesting = flip.Interesting;
            var uuid = flip.Auction.Uuid;

            string seller = null;
            if (socket.Settings.Visibility.Seller)
                seller = await PlayerSearch.Instance.GetNameWithCacheAsync(flip.Auction.AuctioneerId);
            socket.Send(Response.Create("flip", new
            {
                messages = new ChatPart[]{
                new ChatPart(message, openCommand, string.Join('\n', interesting.Select(s => "・" + s)) + "\n" + seller),
                new ChatPart("?", "/cofl reference " + uuid, "Get reference auctions"),
                new ChatPart(" ", openCommand, null)},
                id = uuid,
                worth = flip.Profit,
                cost = flip.Auction.StartingBid,
                sound = (string)null
            }));
            return true;
        }

        public void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }

        public void SendSound(string name, float pitch = 1)
        {
            socket.Send(Response.Create("playSound", new { name, pitch }));
        }
    }
}