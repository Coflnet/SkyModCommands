using System;
using System.Collections.Generic;
using System.Linq;
using hypixel;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public abstract class ModVersionAdapter : IModVersionAdapter
    {
        protected MinecraftSocket socket;

        public abstract Task<bool> SendFlip(FlipInstance flip);
        public abstract void SendMessage(params ChatPart[] parts);
        public abstract void SendSound(string name, float pitch = 1);

        protected async Task<List<ChatPart>> GetMessageparts(FlipInstance flip)
        {
            var openCommand = "/viewauction " + flip.Auction.Uuid;
            var message = socket.GetFlipMsg(flip);
            var interesting = flip.Interesting;
            var extraText = "\n" + String.Join(McColorCodes.DARK_GRAY + ", " + McColorCodes.WHITE, interesting.Take(socket.Settings.Visibility?.ExtraInfoMax ?? 0));

            var uuid = flip.Auction.Uuid;
            var seller = flip.SellerName;
            if (string.IsNullOrEmpty(seller) && (socket.Settings?.Visibility?.Seller ?? false))
                seller = await socket.GetPlayerName(flip.Auction.AuctioneerId);

            var parts = new List<ChatPart>(){
                new ChatPart(message, openCommand, string.Join('\n', interesting.Select(s => "・" + s)) + "\n" + seller),
                new ChatPart(" [?]", "/cofl reference " + uuid, "Get reference auctions"),
                new ChatPart(" ❤", $"/cofl rate {uuid} {flip.Finder} up", "Vote this flip up"),
                new ChatPart("✖ ", $"/cofl rate {uuid} {flip.Finder} down", "Vote this flip down"),
                new ChatPart(extraText, openCommand, null)
            };


            if (socket.Settings.Visibility?.Seller ?? false && flip.SellerName != null)
            {
                parts.Insert(1, new ChatPart(McColorCodes.GRAY + " From: " + McColorCodes.AQUA + flip.SellerName, $"/ah {flip.SellerName}", $"{McColorCodes.GRAY}Open the ah for {McColorCodes.AQUA} {flip.SellerName}"));
            }
            else if (socket.Settings.Visibility?.SellerOpenButton ?? false)
            {
                parts.Insert(1, new ChatPart(McColorCodes.GRAY + " sellers ah", $"/cofl ahopen {flip.Auction}", $"{McColorCodes.GRAY}Open the ah for the seller"));
            }

            return parts;
        }
    }
}