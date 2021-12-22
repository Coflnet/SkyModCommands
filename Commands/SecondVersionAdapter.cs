using System;
using System.Collections.Generic;
using System.Linq;
using hypixel;
using System.Threading.Tasks;
using Coflnet.Sky;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Commands.MC
{
    public class SecondVersionAdapter : IModVersionAdapter
    {
        MinecraftSocket socket;

        public SecondVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
        }

        public async Task<bool> SendFlip(FlipInstance flip)
        {
            var message = socket.GetFlipMsg(flip);
            var openCommand = "/viewauction " + flip.Auction.Uuid;
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
                parts.Insert(1,new ChatPart(McColorCodes.GRAY + " From: " + McColorCodes.AQUA + flip.SellerName, $"/ah {flip.SellerName}", $"{McColorCodes.GRAY}Open the ah for {McColorCodes.AQUA} {flip.SellerName}"));
            }

            SendMessage(parts.ToArray());
 
            if (socket.Settings?.ModSettings?.PlaySoundOnFlip ?? false && flip.Profit > 1_000_000)
                SendSound("note.pling", (float)(1 / (Math.Sqrt((float)flip.Profit / 1_000_000) + 1)));
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