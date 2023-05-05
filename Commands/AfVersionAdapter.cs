using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var purse = socket.SessionInfo.Purse;
            if(purse != 0 && flip.Auction.StartingBid > purse / 3)
            {
                Activity.Current?.SetTag("blocked", "not enough purse");
                return true;
            }
            var name = flip.Auction?.Context?.GetValueOrDefault("cname");
            if (flip.Auction.Count > 1)
                name = $"{McColorCodes.GRAY}{flip.Auction.Count}x {name}";
            socket.Send(Response.Create("flip", new
            {
                id = flip.Auction.Uuid,
                startingBid = flip.Auction.StartingBid,
                purchaseAt = flip.Auction.Start + TimeSpan.FromMilliseconds(19980),
                itemName = name,
                target = flip.MedianPrice
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
            span.Log(JsonConvert.SerializeObject(values));
            span.Log($"Checking sellable {toList.Count()} total {inventory.Count}");
            foreach (var item in socket.LastSent)
            {
                var uid = item.Auction.FlatenedNBT?.FirstOrDefault(y => y.Key == "uid").Value;
                if (uid == null)
                    continue;
                var inventoryRepresent = inventory.Where(x => x != null && x.FlatenedNBT != null && x.FlatenedNBT.TryGetValue("uuid", out var uuid) && uuid.Split('-').Last() == uid).FirstOrDefault();
                if (inventoryRepresent == null)
                    continue;
                var index = inventory.IndexOf(inventoryRepresent);
                if (await ShouldSkip(span, apiService, item.Auction))
                    continue;
                await SendListing(span, item.Auction, item.TargetPrice, index);
                return; // created listing
            }
            foreach (var item in toList)
            {
                var index = inventory.IndexOf(item.First);
                if (await ShouldSkip(span, apiService, item.First))
                    continue;
                await SendListing(span, item.First, item.Second.Median, index);
            }
        }

        private async Task SendListing(Activity span, SaveAuction auction, long price, int index)
        {
            var sellPrice = price * 0.99;
            if (sellPrice < 100_000)
                sellPrice = price;
            span.Log($"Listing {auction.ItemName} for {sellPrice} (median: {price})");
            socket.Send(Response.Create("createAuction", new
            {
                Slot = index,
                Price = sellPrice,
                Duration = 96,
                ItemName = auction.ItemName,
                Id = auction.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value ?? auction.Tag
            }));
            await Task.Delay(3000);
        }

        public override void SendLoginPrompt(string loginLink)
        {
            socket.Dialog(db => db.Msg($"Please §lclick {loginLink} to login"));
        }

        public override void OnAuthorize(AccountInfo accountInfo)
        {
            socket.Dialog(db => db.Msg($"Your session id is {socket.ConSpan.TraceId}, copy that if you encounter an error"));
        }

        private async Task<bool> ShouldSkip(Activity span, IPlayerApi apiService, SaveAuction item)
        {
            var shouldContinue = false;
            var uid = item.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value?.Split('-').Last();
            var foundInSent = socket.LastSent.Any(x => x.Auction.FlatenedNBT.FirstOrDefault(y => y.Key == "uid").Value == uid);
            if (!foundInSent && !string.IsNullOrEmpty(uid))
            {
                var checkFilters = new Dictionary<string, string>() { { "UId", uid }, { "EndAfter", (DateTime.UtcNow - TimeSpan.FromHours(1)).ToUnix().ToString() } };
                var purchases = await apiService.ApiPlayerPlayerUuidBidsGetAsync(socket.SessionInfo.McUuid, 0, checkFilters);
                span.Log($"Found {purchases.Count} purchases of {item.Tag} {item.Uuid}");
                if (purchases.Count == 0)
                    shouldContinue = true; // not bought, keep existing items
            }
            if (item.FlatenedNBT.ContainsKey("donated_museum"))
                shouldContinue = true; // sould bound
            return shouldContinue;
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }
    }
}
