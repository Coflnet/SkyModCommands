using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class AfVersionAdapter : ModVersionAdapter
    {
        DateTime lastListing = DateTime.MinValue;
        public AfVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }
        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var name = flip.Auction.Context["cname"];
            if (flip.Auction.Count > 1)
                name = $"{McColorCodes.GRAY}{flip.Auction.Count}x {name}";
            socket.Send(Response.Create("flip", new
            {
                id = flip.Auction.Uuid,
                startingBid = flip.Auction.StartingBid,
                itemName = name
            }));
            _ = socket.TryAsyncTimes(TryToListAuction, "listAuction", 1);

            return true;
        }

        private async Task TryToListAuction()
        {
            if (DateTime.UtcNow - lastListing < TimeSpan.FromSeconds(15))
                return;
            lastListing = DateTime.UtcNow;
            var apiService = socket.GetService<PlayerApi>();
            var filters = new Dictionary<string, string>() { { "EndfAfter", DateTime.UtcNow.ToUnix().ToString() } };
            var auctions = await apiService.ApiPlayerPlayerUuidAuctionsGetAsync(socket.SessionInfo.McUuid, 1, filters);
            if (auctions.Count > 4)
                return; // ah full
            socket.Send(Response.Create("getInventory", new
            {
                Location = "main"
            }));
            await Task.Delay(1000);
            var inventory = socket.SessionInfo.Inventory ?? new List<SaveAuction>();
            // retrieve price
            var sniperService = socket.GetService<ISniperClient>();
            var values = await sniperService.GetPrices(inventory);
            var toList = inventory.Zip(values).Where(x => x.First != null && x.Second.Median > 0).Take(4 - auctions.Count);
            foreach (var item in toList)
            {
                var index = inventory.IndexOf(item.First);
                socket.Send(Response.Create("createAuction", new
                {
                    Slot = index,
                    Price = item.Second.Median * 0.95,
                    ItemName = item.First.ItemName,
                }));
            }
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }
    }
}
