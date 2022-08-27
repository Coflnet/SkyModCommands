using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

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
                new ChatPart(message, openCommand, socket.formatProvider.GetHoverText(flip)),
                new ChatPart(" ✥ ", "/cofl dialog flipoptions " + uuid, "Expand flip options"),
                //new ChatPart(" ❤", $"/cofl rate {uuid} {flip.Finder} up", "Vote this flip up"),
                //new ChatPart("✖ ", $"/cofl rate {uuid} {flip.Finder} down", "Vote this flip down"),
                new ChatPart(extraText, openCommand, null)
            };


            if ((socket.Settings.Visibility?.Seller ?? false) && !NoSeller(seller))
            {
                parts.Insert(1, new ChatPart(McColorCodes.GRAY + " From: " + McColorCodes.AQUA + seller, $"/ah {seller}", $"{McColorCodes.GRAY}Open the ah for {McColorCodes.AQUA} {flip.SellerName}"));
            }
            else if ((socket.Settings.Visibility?.SellerOpenButton ?? false) || NoSeller(seller))
            {
                var hover = $"{McColorCodes.GRAY}Open the ah for the seller";
                if (seller == "not-found")
                    hover = $"The seller name could not be found. Click to try openening their ah anyways. \nYou can also permanently activate this button instead of the name to improve flip speeds.";
                parts.Insert(1, new ChatPart(McColorCodes.GRAY + " sellers ah", $"/cofl ahopen {flip.Auction.AuctioneerId}", hover));
            }

            return parts;

            static bool NoSeller(string seller)
            {
                return seller == "not-found" || string.IsNullOrEmpty(seller);
            }
        }
    }
}