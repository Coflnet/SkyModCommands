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

namespace Coflnet.Sky.Commands.MC;

public class FullAfVersionAdapter : AfVersionAdapter
{
    protected DateTime lastListing = DateTime.MinValue;

    public FullAfVersionAdapter(MinecraftSocket socket) : base(socket)
    {
    }

    public override async Task TryToListAuction()
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
        activeAuctionCount = (await apiService.ApiPlayerPlayerUuidAuctionsGetAsync(socket.SessionInfo.McUuid, 1, filters)).Count() + 10;
        if (activeAuctionCount >= 14)
        {
            if (listSpace <= 2)
            {
                // get member count
                var res = await socket.GetService<Proxy.Client.Api.IProxyApi>().ProxyHypixelGetAsync($"/skyblock/profiles?uuid={socket.SessionInfo.McUuid}");
                var profiles = JsonConvert.DeserializeObject<ProfilesResponse>(JsonConvert.DeserializeObject<string>(res));
                var profile = profiles.Profiles.FirstOrDefault(x => x.Selected);
                var membersOnIsland = profile.Members.Count;
                listSpace = 14 + 3 * (membersOnIsland - 1);
                using var listLog = socket.CreateActivity("listLog", span);
                listLog.Log($"Auction house fill, {activeAuctionCount} / {listSpace} for {socket.SessionInfo.McName} members {membersOnIsland}");
            }
            if (activeAuctionCount >= listSpace)
            {
                span.Log($"Auction house fill, {activeAuctionCount} / {listSpace} for {socket.SessionInfo.McName}");
                return; // ah full
            }
        }
        await Task.Delay(TimeSpan.FromSeconds(2));
        var inventory = socket.SessionInfo.Inventory;
        if (inventory == null)
        {
            socket.Dialog(db => db.Msg(McColorCodes.RED + "No inventory uploaded, can't list auctions, no clue what client you use but its either outdated or broken. Please contact Äkwav#0421 on discord and include " + socket.ConSpan.TraceId));
            throw new Exception("no inventory");
        }
        // retrieve price
        var sniperService = socket.GetService<ISniperClient>();
        var values = await sniperService.GetPrices(inventory);
        var toList = inventory.Zip(values).Skip(9).Where(x => x.First != null && x.Second.Median > 1000);
        span.Log(JsonConvert.SerializeObject(toList));
        foreach (var item in socket.LastSent.Where(x => x.Finder != LowPricedAuction.FinderType.USER))
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
        span.Log($"Checking sellable {toList.Count()} total {inventory.Count}");
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
            if(socket.LastSent.Any(x => x.Auction.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value == uuid))
                continue; // ignore recently sent they are handled by the loop above
            // get target 
            var flips = await GetFlipData(await GetPurchases(apiService, uuid));
            var target = (flips.Select(f => (long)f.TargetPrice).DefaultIfEmpty(item.Second.Median).Average() + item.Second.Median) / 2;
            if (flips.Count == 0)
            {
                if (!socket.SessionInfo.SellAll)
                {
                    Activity.Current?.SetTag("state", "no sent flips").Log(JsonConvert.SerializeObject(item.First));
                    socket.Dialog(db => db.Msg($"Found unknown item in inventory: {item.First.ItemName} {item.First.Tag} {item.First.Uuid} could have been whitelisted, please manually remove it from inventory or execute {McColorCodes.AQUA}/cofl sellinventory"));
                    continue;
                }
                target = item.Second.Median;
            }
            else if (flips.All(x => x.Timestamp > DateTime.UtcNow.AddDays(-2)))
            {
                // all are more recent than a day, still usable
                target = flips.Select(f => f.TargetPrice).Average();
                span.Log($"Found {flips.Count} flips for target {target}");
            }
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


    public override void OnAuthorize(AccountInfo accountInfo)
    {
        socket.Dialog(db => db.Msg($"Your connection id is {socket.ConSpan.TraceId}, copy that if you encounter an error"));
        socket.sessionLifesycle.SessionInfo.FlipsEnabled = true;
        socket.SessionInfo.IsMacroBot = true;
        if (socket.sessionLifesycle.FlipSettings.Value?.ModSettings?.AutoStartFlipper == null)
            return;
        socket.sessionLifesycle.FlipSettings.Value.ModSettings.AutoStartFlipper = true;
        socket.sessionLifesycle.FlipSettings.Value.Visibility.Seller = false;
    }

    private async Task<bool> ShouldSkip(Activity span, IPlayerApi apiService, SaveAuction item)
    {
        var uid = item.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid" || y.Key == "uid").Value?.Split('-').Last();
        var foundInSent = socket.LastSent.Any(x => x.Auction.FlatenedNBT.FirstOrDefault(y => y.Key == "uid").Value == uid);
        if (foundInSent)
            return false;
        if (item.FlatenedNBT.ContainsKey("donated_museum"))
            return true; // sould bound
                         // ⬇⬇ sell able items ⬇⬇
        if (socket.SessionInfo.SellAll)
            return false;
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
        span.Log($"No uuid found, can't determine skip status of {item.Tag} {item.Uuid}");
        return true;
    }

    private async Task<List<FlipTracker.Client.Model.Flip>> GetFlipData(List<Api.Client.Model.BidResult> purchases)
    {
        var purchase = purchases.OrderByDescending(x => x.End).FirstOrDefault();
        if (purchase == null)
            return new List<FlipTracker.Client.Model.Flip>();
        var longId = socket.GetService<AuctionService>().GetId(purchase.AuctionId);
        return await socket.GetService<ITrackerApi>().TrackerFlipsAuctionIdGetAsync(longId);
    }
    private async Task<List<Api.Client.Model.BidResult>> GetPurchases(IPlayerApi apiService, string uid)
    {
        if (CheckedPurchase.GetValueOrDefault(uid) > 3)
            return new List<Api.Client.Model.BidResult>();
        var checkFilters = new Dictionary<string, string>() {
                { "UId", uid },
                { "EndAfter", (DateTime.UtcNow - TimeSpan.FromHours(48)).ToUnix().ToString() } };
        var purchases = await apiService.ApiPlayerPlayerUuidBidsGetAsync(socket.SessionInfo.McUuid, 0, checkFilters);
        CheckedPurchase[uid] = CheckedPurchase.GetValueOrDefault(uid) + 1;
        return purchases;
    }
}
