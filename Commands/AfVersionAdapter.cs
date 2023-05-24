using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.FlipTracker.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class AfVersionAdapter : ModVersionAdapter
    {
        DateTime lastListing = DateTime.MinValue;
        private int membersOnIsland = 0;
        public AfVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }
        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var purse = socket.SessionInfo.Purse;
            if (purse != 0 && flip.Auction.StartingBid > purse / 3)
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
            Activity.Current?.SetTag("finder", flip.Finder);
            Activity.Current?.SetTag("target", flip.MedianPrice);
            Activity.Current?.SetTag("itemName", name);

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
            socket.Send(Response.Create("getInventory", new
            {
                Location = "main"
            }));
            var auctions = await apiService.ApiPlayerPlayerUuidAuctionsGetAsync(socket.SessionInfo.McUuid, 1, filters);
            if (auctions.Count >= 4)
            {
                if (membersOnIsland == 0)
                {
                    // get member count
                    var res = await socket.GetService<Proxy.Client.Api.IProxyApi>().ProxyHypixelGetAsync($"/skyblock/profiles?uuid={socket.SessionInfo.McUuid}");
                    var profiles = JsonConvert.DeserializeObject<ProfilesResponse>(JsonConvert.DeserializeObject<string>(res));
                    var profile = profiles.Profiles.FirstOrDefault(x => x.Selected);
                    if (profile != null)
                        membersOnIsland = profile.Members.Count;
                }
                var listSpace = 4 + 3 * (membersOnIsland - 1);
                if (auctions.Count >= listSpace)
                    return; // ah full
                dev.Logger.Instance.Log($"Auction house full, {auctions.Count} / {listSpace} for {socket.SessionInfo.McName} members {membersOnIsland}");
                return; // security check
            }
            await Task.Delay(800);
            var inventory = socket.SessionInfo.Inventory;
            if (inventory == null)
            {
                socket.Dialog(db => db.Msg(McColorCodes.RED + "No inventory uploaded, can't list auctions, no clue what client you use but its either outdated or broken. Please contact Äkwav#0421 on discord"));
                throw new Exception("no inventory");
            }
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
                var uuid = GetUuid(inventoryRepresent);
                await SendListing(span, item.Auction, item.TargetPrice, index, uuid);
                return; // created listing
            }
            foreach (var item in toList)
            {
                var index = inventory.IndexOf(item.First);
                if (await ShouldSkip(span, apiService, item.First))
                    continue;
                var uuid = GetUuid(item.First);
                if (uuid == null)
                {
                    Activity.Current?.SetTag("error", "no uuid").Log(JsonConvert.SerializeObject(item.First));
                    continue;
                }
                // get target 
                var flips = await GetFlipData(await GetPurchases(apiService, uuid));
                var target = (flips.Select(f => f.TargetPrice).Average() + item.Second.Median) / 2;
                await SendListing(span, item.First, (long)target, index, uuid);
            }
        }

        private static string GetUuid(SaveAuction inventoryRepresent)
        {
            return inventoryRepresent.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value;
        }

        private async Task SendListing(Activity span, SaveAuction auction, long price, int index, string uuid)
        {
            var roundTarget = price > 5_000_000 ? 100_000 : 10_000;
            long sellPrice = (long)(price * 0.99) / roundTarget * roundTarget;
            if (Random.Shared.NextDouble() < 0.3)
                sellPrice -= 1;
            else if (Random.Shared.NextDouble() < 0.3)
                sellPrice -= 1000;
            if (sellPrice < 100_000)
                sellPrice = price;
            var id = uuid ?? auction.Tag;
            span.Log($"Listing {auction.ItemName} for {sellPrice} (median: {price}) slot {index} id: {id}");
            socket.Send(Response.Create("createAuction", new
            {
                Slot = index,
                Price = sellPrice,
                Duration = 96,
                ItemName = auction.ItemName,
                Id = id
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
            socket.sessionLifesycle.SessionInfo.FlipsEnabled = true;
            socket.SessionInfo.IsMacroBot = true;
            if (socket.sessionLifesycle.FlipSettings.Value?.ModSettings?.AutoStartFlipper == null)
                return;
            socket.sessionLifesycle.FlipSettings.Value.ModSettings.AutoStartFlipper = true;
            socket.sessionLifesycle.FlipSettings.Value.Visibility.Seller = false;
        }

        private async Task<bool> ShouldSkip(Activity span, IPlayerApi apiService, SaveAuction item)
        {
            var uid = item.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value?.Split('-').Last();
            var foundInSent = socket.LastSent.Any(x => x.Auction.FlatenedNBT.FirstOrDefault(y => y.Key == "uid").Value == uid);
            if (foundInSent)
                return false;
            if (item.FlatenedNBT.ContainsKey("donated_museum"))
                return true; // sould bound
            if (!string.IsNullOrEmpty(uid))
            {
                List<Api.Client.Model.BidResult> purchases = await GetPurchases(apiService, uid);
                span.Log($"Found {purchases.Count} purchases of {item.Tag} {item.Uuid}");
                if (purchases.Count == 0)
                    return true; // not bought, keep existing items
                else
                {
                    var flipData = await GetFlipData(purchases);
                    var target = flipData.OrderBy(f => f.Timestamp).Select(f => f.TargetPrice).FirstOrDefault();
                    Activity.Current?.Log($"Found {flipData.Count} flips for target {target}");
                    return !flipData.Any();
                }
            }
            return true;
        }

        private async Task<List<FlipTracker.Client.Model.Flip>> GetFlipData(List<Api.Client.Model.BidResult> purchases)
        {
            var purchase = purchases.OrderByDescending(x => x.End).First();
            var longId = socket.GetService<AuctionService>().GetId(purchase.AuctionId);
            return await socket.GetService<ITrackerApi>().TrackerFlipsAuctionIdGetAsync(longId);
        }
        private async Task<List<Api.Client.Model.BidResult>> GetPurchases(IPlayerApi apiService, string uid)
        {
            var checkFilters = new Dictionary<string, string>() {
                { "UId", uid },
                { "EndAfter", (DateTime.UtcNow - TimeSpan.FromHours(48)).ToUnix().ToString() } };
            var purchases = await apiService.ApiPlayerPlayerUuidBidsGetAsync(socket.SessionInfo.McUuid, 0, checkFilters);
            return purchases;
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }

        public override void SendSound(string name, float pitch = 1)
        {
            // ignore
        }
    }

    public class ProfilesResponse
    {
        public List<Profile> Profiles { get; set; }
    }

    public class Profile
    {
        public bool Selected { get; set; }
        public Dictionary<string, object> Members { get; set; }
    }
}
