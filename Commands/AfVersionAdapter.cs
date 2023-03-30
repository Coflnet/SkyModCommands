using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

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
            using var span = socket.CreateActivity("listAuction", socket.ConSpan);
            lastListing = DateTime.UtcNow;
            var apiService = socket.GetService<IPlayerApi>();
            var filters = new Dictionary<string, string>() { { "EndAfter", DateTime.UtcNow.ToUnix().ToString() } };
            var auctions = await apiService.ApiPlayerPlayerUuidAuctionsGetAsync(socket.SessionInfo.McUuid, 1, filters);
            if (auctions.Count >= 4)
                return; // ah full
            socket.Send(Response.Create("getInventory", new
            {
                Location = "main"
            }));
            await Task.Delay(800);
            var inventory = socket.SessionInfo.Inventory ?? throw new Exception("no inventory");
            // retrieve price
            var sniperService = socket.GetService<ISniperClient>();
            var values = await sniperService.GetPrices(inventory);
            var toList = inventory.Zip(values).Where(x => x.First != null && x.Second.Median > 1000);
            span.Log(JsonConvert.SerializeObject(socket.LastSent));
            foreach (var item in toList)
            {
                var index = inventory.IndexOf(item.First);
                var uid = item.First.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value?.Split('-').Last();
                var foundInSent = socket.LastSent.Any(x => x.Auction.FlatenedNBT.FirstOrDefault(y => y.Key == "uid").Value == uid);
                if (!foundInSent && !string.IsNullOrEmpty(uid))
                {
                    filters = new Dictionary<string, string>() { { "Uid", uid }, { "EndAfter", (DateTime.UtcNow - TimeSpan.FromHours(1)).ToUnix().ToString() } };
                    var purchases = await apiService.ApiPlayerPlayerUuidBidsGetAsync(socket.SessionInfo.McUuid, 0, filters);
                    span.Log($"Found {purchases.Count} purchases of {item.First.ItemName}");
                    if (purchases.Count == 0)
                        continue; // not bought, keep existing items
                }
                if(item.First.FlatenedNBT.ContainsKey("donated_museum"))
                    continue; // sould bound
                span.Log($"Listing {item.First.ItemName} for {item.Second.Median * 0.95} (median: {item.Second.Median})");
                socket.Send(Response.Create("createAuction", new
                {
                    Slot = index,
                    Price = item.Second.Median * 0.95,
                    Duration = 96,
                    ItemName = item.First.ItemName,
                }));
                break;
            }
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }
    }
}
