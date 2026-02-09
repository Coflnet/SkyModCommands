using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.FlipTracker.Client.Api;
using Coflnet.Sky.FlipTracker.Client.Model;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SaveAuction = Coflnet.Sky.Core.SaveAuction;

namespace Coflnet.Sky.Commands.MC;

public class FullAfVersionAdapter : AfVersionAdapter
{
    protected DateTime lastListing = DateTime.MinValue;
    protected DateTime lastInventoryFullMsg = DateTime.MinValue;

    public FullAfVersionAdapter(MinecraftSocket socket) : base(socket)
    {
        socket.SessionInfo.IsMacroBot = true;
    }

    public override async Task<bool> SendFlip(FlipInstance flip)
    {
        var result = await base.SendFlip(flip);
        var uuid = GetUuid(flip.Auction);
        if (uuid == null)
            return result;
        return result;
    }

    public override async Task TryToListAuction()
    {
        if (socket.CurrentRegion != "eu")
            return; // only works with direct db access
        using var span = socket.CreateActivity("listAuctionTry", socket.ConSpan);
        await UpdateAhSlots(span);
        if (socket.Version[0] == '1')
        {
            if (socket.SessionInfo.SellAll)
                socket.Dialog(db => db.Msg("BAF versions older than 2.0.0 don't get relist recommendations anymore"));
            return;
        }
        await Task.Delay(5000);
        if (DateTime.UtcNow - lastListing < TimeSpan.FromSeconds(15) || socket.CurrentRegion != Region.EU)
            return;
        lastListing = DateTime.UtcNow;
        var apiService = socket.GetService<IPlayerApi>();
        var filters = new Dictionary<string, string>() { { "EndAfter", DateTime.UtcNow.ToUnix().ToString() } };
        RequestInventory();
        if (activeAuctionCount >= 14)
        {

            if (activeAuctionCount >= listSpace)
            {
                span.Log($"Auction house fill, {activeAuctionCount} / {listSpace} for {socket.SessionInfo.McName}");

                if (Random.Shared.NextDouble() < 0.3)
                {
                    socket.Dialog(db => db.Msg("Auction house full, waiting for something to sell or expire before listing another auction"));
                    socket.Send(Response.Create("collectAuctions", new { }));
                }
                return; // ah full
            }
        }
        List<SaveAuction> inventory = await WaitForInventory();
        if (inventory.Count == 0)
            return;
        // retrieve price
        var sniperService = socket.GetService<ISniperClient>();
        var values = await sniperService.GetPrices(inventory);
        var toList = inventory.Zip(values).Skip(9).Where(x => x.First != null && x.Second.Median > 1000).ToList();
        using (var dataResult = socket.CreateActivity("listData", span))
            dataResult.Log(JsonConvert.SerializeObject(toList));
        foreach (var item in LastSentFlips())
        {
            var itemUuid = item.Auction.FlatenedNBT?.FirstOrDefault(y => y.Key == "uuid").Value;
            if (itemUuid == null)
                continue;
            var inventoryRepresent = inventory.Where(x => x != null && x.FlatenedNBT != null && x.FlatenedNBT.TryGetValue("uuid", out var uuid) && uuid == itemUuid).FirstOrDefault();
            if (inventoryRepresent == null)
                continue;
            var index = inventory.IndexOf(inventoryRepresent);
            if (await ShouldSkip(span, apiService, item.Auction))
                continue;
            var uuid = GetUuid(inventoryRepresent);

            using var listingSpan = socket.CreateActivity("listAuction", span);
            listingSpan?.SetTag("uuid", uuid);
            listingSpan.Log(JsonConvert.SerializeObject(item));

            // Check if item was changed since purchase
            var wasItemChanged = ItemComparisonHelper.WasItemChanged(item.Auction, inventoryRepresent);
            if (wasItemChanged)
            {
                listingSpan.Log($"Item {item.Auction.ItemName} was changed since purchase, using market-based pricing only {JsonConvert.SerializeObject(item)} {JsonConvert.SerializeObject(inventoryRepresent)}");
            }

            var marketBased = toList.Where(x => x.First.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value == uuid
                && PriceIsNoGuess(x))
                .Select(x => Math.Min(x.Second.Median, x.Second.Lbin.Price)).FirstOrDefault();
            var stored = await socket.GetService<IPriceStorageService>().GetPrice(Guid.Parse(socket.SessionInfo.McUuid), Guid.Parse(uuid));

            double targetPrice = Math.Max(item.TargetPrice, Math.Max(marketBased * 0.95, stored));
            if (wasItemChanged)
            {
                // Item was changed, don't use stored estimate or flip target price
                var marketBasedNew = marketBased > 0 ? marketBased * 0.95 : toList.Where(x => x.First.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value == uuid)
                    .Select(x => x.Second.Median).FirstOrDefault();
                listingSpan.Log($"Item changed, using market price {targetPrice} instead of stored {stored} or target {item.TargetPrice}");
                targetPrice = marketBasedNew;
            }

            if (stored < 0)
                continue; // user finder/do not relist
            listingSpan.Log($"Found {item.Auction.ItemName} {item.Auction.Tag} {item.Auction.Uuid} in sent with price {targetPrice} stored {stored}, marked {marketBased}");
            await SendListing(listingSpan, item.Auction, (long)targetPrice, index, uuid);
            return; // created listing
        }
        span.Log($"Checking sellable {toList.Count()} total {inventory.Count}. Space {listSpace} active {activeAuctionCount}");
        await ListItems(span, apiService, inventory, toList, listSpace < 10 ? 14 : listSpace - activeAuctionCount);

        static bool PriceIsNoGuess((SaveAuction First, Sniper.Client.Model.PriceEstimate Second) x)
        {
            return x.Second.MedianKey == x.Second.ItemKey;
        }
    }

    private async Task UpdateAhSlots(Activity span)
    {
        using (var context = new HypixelContext())
        {
            var profile = await context.Players.FindAsync(socket.SessionInfo.McUuid);
            activeAuctionCount = await context.Auctions.Where(a => a.SellerId == profile.Id && a.End > DateTime.UtcNow).CountAsync();
        }
        if (listSpace <= 2)
        {
            await UpdateListSpace(span);
        }
        // set for configs
        socket.SessionInfo.AhSlotsOpen = listSpace - activeAuctionCount;
    }

    private IEnumerable<Core.LowPricedAuction> LastSentFlips()
    {
        return socket.LastSent
                // Flips can be relisted for a different price, 
                // by ordering by start the last listing is chosen
                .OrderByDescending(ls => ls.Auction.Start)
                .Where(x => x.Finder != Core.LowPricedAuction.FinderType.USER);
    }

    private async Task<List<SaveAuction>> WaitForInventory()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        for (int i = 0; i < 8; i++)
        {
            if (socket.SessionInfo.Inventory != null)
                break;
            RequestInventory();
            await Task.Delay(1000);
        }
        var inventory = socket.SessionInfo.Inventory;
        if (inventory == null)
        {
            socket.Dialog(db => db.Msg(McColorCodes.RED + "No inventory uploaded, can't list auctions, no clue what client you use but its either outdated or broken. Please contact Äkwav#0421 on discord and include " + socket.ConSpan.TraceId));
            return [];
        }

        return inventory;
    }

    private async Task UpdateListSpace(Activity span)
    {
        using var listLog = socket.CreateActivity("listLog", span);
        try
        {
            // get member count
            var res = await socket.GetService<Proxy.Client.Api.IProxyApi>().ProxyHypixelGetAsync($"/v2/skyblock/profiles?uuid={socket.SessionInfo.McUuid}");
            if (res == null)
                throw new CoflnetException("proxy_error", "Could not check how many coop members you have, if this persists please contact support");
            var profiles = JsonConvert.DeserializeObject<ProfilesResponse>(JsonConvert.DeserializeObject<string>(res));
            if (profiles?.Profiles == null)
                throw new CoflnetException("proxy_error", "Could not check how many coop members you have, if this persists please contact support");
            var profile = profiles.Profiles.FirstOrDefault(x => x.Selected);
            var membersOnIsland = profile.Members.Where(m => m.Value.Profile.DeletionNotice?.TimeStamp == null).Count();
            listSpace = 14 + 3 * (membersOnIsland - 1);
            listLog.Log($"Auction house fill, {activeAuctionCount} / {listSpace} for {socket.SessionInfo.McName} members {membersOnIsland}");
        }
        catch (Exception e)
        {
            socket.GetService<ILogger<FullAfVersionAdapter>>().LogError(e, "updating list space");
            listLog.Log("Error updating list space");
        }
    }

    private async Task ListItems(Activity span, IPlayerApi apiService, List<SaveAuction> inventory, IEnumerable<(SaveAuction First, Sniper.Client.Model.PriceEstimate Second)> toList, int space)
    {
        var listed = 0;
        var skipped = 0;
        foreach (var item in toList.OrderByDescending(x => x.Second.Volume).ThenByDescending(x => x.Second.Median))
        {
            if (listed >= space)
                break;
            var index = inventory.IndexOf(item.First);
            if (await ShouldSkip(span, apiService, item.First))
            {
                skipped++;
                continue;
            }
            if (item.First.Tier == Core.Tier.COMMON && !item.First.Tag.StartsWith("PET"))
            {
                var itemsApi = socket.GetService<Items.Client.Api.IItemsApi>();
                var getDetails = await itemsApi.ItemItemTagGetAsync(item.First.Tag);
                if (getDetails.NpcSellPrice >= 1)
                {
                    if (!socket.SessionInfo.SellAll)
                        continue;
                    socket.Dialog(db => db.Msg($"Found a `common` item in inventory: {item.First.ItemName} {item.First.Tag} it probably has to be auctioned, please manually create an auction for it. Or report if it can be bin auctioned"));
                    continue;
                }
            }
            if (item.Second.MedianKey == null)
            {
                if (socket.Version.StartsWith("af-3") && await HotkeyCommand.IsOnBazaar(socket, item.First.Tag))
                {
                    await RecommendBazaarSellOrder(item.First.Tag, item.First.ItemName, item.First.Count);
                    continue;
                }
                socket.Dialog(db => db.MsgLine($"There was no ah price found for {item.First.ItemName} {item.First.Tag}, this item can't be sold. It takes up space in your inventory until you remove it"));
                continue;
            }
            var uuid = GetUuid(item.First);
            if (uuid == null)
            {
                Activity.Current?.SetTag("error", "no uuid").Log(JsonConvert.SerializeObject(item.First));
                // try to find in sent by name
                var fromSent = socket.LastSent.Where(x => GetItemName(x.Auction).Replace("§8!", "").Replace("§8.", "") == item.First.ItemName && x.Auction.Tag == item.First.Tag)
                    .OrderByDescending(i => i.TargetPrice).FirstOrDefault();
                var price = fromSent?.TargetPrice ?? (item.Second.Median > item.Second.Lbin.Price ? item.Second.Median : (item.Second.Lbin.Price + item.Second.Median) / 2);
                using var stackableSpan = socket.CreateActivity("listAuction", span);
                if (fromSent != null && item.First.Count == fromSent.Auction.Count)
                {
                    price = Math.Max(price, item.Second.Median * 95 / 100);
                    stackableSpan.Log($"Found {fromSent.Auction.ItemName} in sent using price {price}");
                }
                else if (item.First.Count > 1)
                {
                    try
                    {

                        long estimate = await GetEstimateViaLastPurchasedNoUid(stackableSpan, apiService, item);
                        span.Log($"Nouid {item.First.ItemName} x{item.First.Count} using price {estimate}");
                        price = estimate;
                    }
                    catch (System.Exception e)
                    {
                        socket.Error(e, "checking no uid");
                        continue;
                    }
                }
                else
                {
                    stackableSpan.Log($"No uuid found for {item.First.ItemName} {item.First.Tag} using price {price}");
                    stackableSpan.Log(JsonConvert.SerializeObject(socket.LastSent.Select(s => new { s.Auction.Tag, s.Auction.Uuid, name = GetItemName(s.Auction), s.Auction.Count })));
                }
                await SendListing(stackableSpan, item.First, price, index, uuid);
                listed++;
                break; // only list one without uuid
            }
            if (socket.LastSent.Any(x => x.Auction.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid").Value == uuid))
                continue; // ignore recently sent they are handled by the loop above
            // get target 
            using var listingSpan = socket.CreateActivity("listAuction", span);
            (bool abort, double target) = await GetPrice(apiService, item, uuid, listingSpan);
            if (!abort)
            {
                listingSpan.Log("triggering abort");
                skipped++;
                continue;
            }
            listingSpan?.SetTag("uuid", uuid);
            await SendListing(listingSpan, item.First, (long)target, index, uuid);
            listed++;
        }
        if (skipped == toList.Count() && skipped > 10)
        {
            socket.Dialog(db => db.MsgLine($"Skipped {skipped} items in inventory because they were either bought a long time ago, soulbound or marked to be not relisted.")
                .MsgLine($"Note that having many of those items can fill up your inventory and prevent receiving flips, please remove them, you can sell them at market prices with {McColorCodes.AQUA}/cofl sellinventory{McColorCodes.GRAY}"));
        }
    }

    private async Task<(bool flowControl, double value)> GetPrice(IPlayerApi apiService, (SaveAuction First, Sniper.Client.Model.PriceEstimate Second) item, string uuid, Activity listingSpan)
    {
        var storedEstimate = socket.GetService<IPriceStorageService>().GetPrice(Guid.Parse(socket.SessionInfo.McUuid), Guid.Parse(uuid));
        var purchases = await GetItemPurchases(apiService, uuid);
        var wasItemChanged = await WasItemChangedSincePurchase(purchases, item.First);
        if (wasItemChanged)
        {
            listingSpan.Log($"Item {item.First.ItemName} was changed since purchase, will use market-based pricing only");
        }
        var flips = await GetFlipData(purchases);
        var target = (flips.Select(f => (long)f.TargetPrice).DefaultIfEmpty(item.Second.Median).Average() + item.Second.Median) / 2;
        if (flips.Count == 0)
        {
            if (!socket.SessionInfo.SellAll)
            {
                Activity.Current?.SetTag("state", "no sent flips").Log(JsonConvert.SerializeObject(item.First));
                socket.Dialog(db => db.Msg($"Found unknown item in inventory: {item.First.ItemName} {item.First.Tag} {item.First.Uuid} could have been whitelisted, please manually remove it from inventory or execute {McColorCodes.AQUA}/cofl sellinventory"));
                return (flowControl: false, value: default);
            }
            listingSpan.Log($"keys:{item.Second.MedianKey}\n{item.Second.ItemKey}");
            target = item.Second.Median;
            if (item.Second.MedianKey != item.Second.ItemKey)
            {
                target = await CheckForExpensiveCraftCost(item, listingSpan, target);
            }
        }
        else if (flips.All(x => x.Timestamp > DateTime.UtcNow.AddDays(-2)) && !wasItemChanged)
        {
            // all are more recent than a day and item not changed, still usable
            target = flips.Where(f => (int)f.FinderType < 100 && IsFinderEnabled(f))
                    .Select(f => f.TargetPrice).DefaultIfEmpty((int)flips.Select(f => f.TargetPrice).Average()).Average();
            listingSpan.Log($"Found {flips.Count} flips for average price {target}");
        }
        else if (wasItemChanged)
        {
            listingSpan.Log($"Found {flips.Count} flips but item was changed since purchase, using median {item.Second.Median}");
            target = item.Second.Median;
        }
        else
        {
            listingSpan.Log($"Found {flips.Count} flips but they are too old, using mostly median {item.Second.Median}");
        }
        var checkFilters = new Dictionary<string, string>() {
                { "UId", uuid },
                { "EndAfter", (DateTime.UtcNow - TimeSpan.FromDays(4)).ToUnix().ToString() } };
        var sellAttempts = await apiService.ApiPlayerPlayerUuidAuctionsGetAsync(socket.SessionInfo.McUuid, 0, checkFilters);
        var stored = await storedEstimate;
        if (stored < 0)
        {
            listingSpan.Log($"Stored price for {item.First.ItemName} was {target} {stored}");
            if (!socket.SessionInfo.SellAll)
                return (flowControl: false, value: default);
            var foundReason = stored == -1 ? "found by USER finder" : "marked with not list filter";
            socket.Dialog(db => db.Msg($"Because you executed {McColorCodes.AQUA}/cofl sellinventory{McColorCodes.GRAY} item {item.First.ItemName} {foundReason} will be sold at market price"));
        }
        if (stored > 200_000 && !wasItemChanged)
        {
            listingSpan.Log($"Found stored price for {item.First.ItemName} {item.First.Tag} {item.First.Uuid} using price {stored} instead of {target}");
            target = stored;
            if (sellAttempts.Count > 0)
            {
                var reduction = Math.Max(0.4, 1 - ((double)sellAttempts.Count * 0.05));
                listingSpan.Log($"Found {sellAttempts.Count} attempts to sell, reducing target by {reduction}");
                target *= reduction;
            }
        }
        else if (stored > 200_000 && wasItemChanged)
        {
            listingSpan.Log($"Found stored price {stored} for {item.First.ItemName} but item was changed since purchase, using market price {target} instead");
        }
        // list at least at 90% of median
        target = Math.Max(target, item.Second.Median * 0.9);

        if (socket.Settings.ModSettings.QuickSell)
        {
            target = SniperClient.InstaSellPrice(item.Second).Item1 * (item.Second.Volume > 5 ? 1 : 0.98);
            listingSpan.Log("Set instasell price to " + target);
            socket.Dialog(db => db.MsgLine($"{McColorCodes.DARK_RED} [QuickSelling] {McColorCodes.GRAY} {item.First.ItemName} {McColorCodes.GRAY} for {McColorCodes.GOLD} {target}.")
                .MsgLine($"{McColorCodes.GRAY}Might be undervalued use {McColorCodes.AQUA}/cofl set quicksell false{McColorCodes.GRAY} to disable"));
            await Task.Delay(2000);
            if (!socket.Settings.ModSettings.QuickSell)
                return (flowControl: false, value: default);
        }

        return (flowControl: true, value: target);
    }

    private async Task<double> CheckForExpensiveCraftCost((SaveAuction First, Sniper.Client.Model.PriceEstimate Second) item, Activity listingSpan, double target)
    {
        // does not match check craft cost to avoid undervaluing
        var breakdown = await socket.GetService<IModApi>().ApiModPricingBreakdownPostAsync(new() { new(){
                    Count = item.First.Count,
                    Enchantments = item.First.Enchantments.GroupBy(e=>e.Type).Select(g=>g.First()).ToDictionary( e=>e.Type.ToString(), e=>(int)e.Level),
                    ExtraAttributes = item.First.FlatenedNBT.ToDictionary(y => y.Key, y => (object)y.Value),
                    ItemName = item.First.ItemName,
                    Tag = item.First.Tag,
                } });
        var valueSum = breakdown.Sum(b => b.CraftPrice.Sum(c => c.Price));
        var possibleSellAt = valueSum / 3;
        if (possibleSellAt > target)
        {
            listingSpan.Log($"The craft cost of {item.First.ItemName} {item.First.Tag} is more than 3x higher than the closest median at {valueSum}, using 1/3rd of craft cost as target");
            target = possibleSellAt;
        }

        return target;
    }

    private bool IsFinderEnabled(Flip f)
    {
        var finderString = f.FinderType.ToString();
        return socket.Settings.AllowedFinders.HasFlag(Enum.Parse<Core.LowPricedAuction.FinderType>(finderString switch
        {
            "SNIPERMEDIAN" => "SNIPER_MEDIAN",
            _ => finderString
        }));
    }

    private async Task<long> GetEstimateViaLastPurchasedNoUid(Activity span, IPlayerApi apiService, (SaveAuction First, Sniper.Client.Model.PriceEstimate Second) item)
    {
        // find via history 
        var history = await apiService.ApiPlayerPlayerUuidBidsGetAsync(socket.SessionInfo.McUuid, 0, new Dictionary<string, string>() { { "tag", item.First.Tag }, { "EndAfter", (DateTime.UtcNow - TimeSpan.FromDays(14)).ToUnix().ToString() } });
        var matching = history.OrderByDescending(x => (x.ItemName == item.First.ItemName) ? 1 : 0).ThenByDescending(x => x.End).Where(x => x.End > DateTime.UtcNow.AddDays(-2));
        var count = matching.Count();
        var historyItem = matching.FirstOrDefault();
        if (count > 1)
        {
            var auction = await apiService.ApiPlayerPlayerUuidAuctionsGetAsync(historyItem.AuctionId);
            if (auction.Count != item.First.Count)
                throw new CoflnetException("multiple_history_items", $"Your last purchase of {item.First.Count} {item.First.ItemName} not matching the {item.First.Count} in your inventory. To prevent underlisting please list manually");
        }
        var auctionId = socket.GetService<AuctionService>().GetId(historyItem.AuctionId);
        // get price from fliptracker
        var flipData = await socket.GetService<ITrackerApi>().GetFlipsOfAuctionAsync(auctionId);
        var estimate = (long)flipData.Select(f => f.TargetPrice).Average();
        span.Log($"Found {item.First.ItemName} in inventory with count {item.First.Count} using price {estimate}");
        return estimate;
    }

    private void RequestInventory()
    {
        socket.Send(Response.Create("getInventory", new
        {
            Location = "main"
        }));
    }

    protected override (bool skip, bool wait) ShouldStopBuying()
    {
        var maxItemsAllowedInInventory = socket.Settings?.ModSettings?.MaxFlipItemsInInventory ?? 0;
        if (maxItemsAllowedInInventory != 0
        // < because menu is always there
            && maxItemsAllowedInInventory < socket.SessionInfo.Inventory?.Skip(10).Where(x => x != null).Count())
        {
            if (lastInventoryFullMsg < DateTime.UtcNow.AddMinutes(-5))
            {
                socket.Dialog(db => db.Msg($"Reached max flip items in inventory ({maxItemsAllowedInInventory}), paused buying until items are sold and listed. ")
                    .Msg($"Can be disabled with {McColorCodes.AQUA}/cofl set maxItemsInInventory 0"));
                lastInventoryFullMsg = DateTime.UtcNow;
                socket.TryAsyncTimes(TryToListAuction, "listAuction", 1);
            }
            return (true, false);
        }
        var spaceLeft = socket.SessionInfo.Inventory?.Skip(10).Count(x => x == null);
        var isFull = spaceLeft < 3;
        if (maxItemsAllowedInInventory >= 100)
        {
            // special case user wants to not stop buying
            if (Random.Shared.NextDouble() < 0.3)
                RequestInventory();
            return (false, isFull || socket.SessionInfo.Inventory == null);
        }
        if (isFull)
        {
            if (Random.Shared.NextDouble() < 0.1)
                socket.Dialog(db => db.MsgLine("§cAuction house and inventory full, paused buying"));
            if (Random.Shared.NextDouble() < 0.3)
                RequestInventory();
        }
        else if (spaceLeft < 10 && Random.Shared.NextDouble() < 0.9 / spaceLeft)
            return (false, true); // randomly wait so an item is still bought every once in a while
        return (isFull, false);
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
        var id = uuid ?? MapToGameTag(auction);
        var name = auction?.Context?.GetValueOrDefault("cname") ?? auction.ItemName;
        span.Log($"Listing {name} for {sellPrice} (median: {price}) slot {index} id: {id}");
        var listTime = socket.Settings?.ModSettings?.AhListTimeTarget;
        if (listTime == 0)
            listTime = null;
        if (sellPrice == 0)
        {
            socket.Error(new(), "Price is 0, skipping listing, og: " + price, JsonConvert.SerializeObject(auction));
            return;
        }
        socket.Send(Response.Create("createAuction", new
        {
            Slot = index,
            Price = sellPrice,
            Duration = listTime ?? 96,
            ItemName = name,
            Id = id
        }));
        await Task.Delay(5500);
        if (socket.SessionInfo.ToLowListingAttempt == null)
            return;
        await RetryListingWithMinimum(span, auction, index, sellPrice, id, listTime);
    }

    private async Task RetryListingWithMinimum(Activity span, SaveAuction auction, int index, long sellPrice, string id, int? listTime)
    {
        // sample string:You must set it to at least 1,500,000!
        var parsed = int.Parse(socket.SessionInfo.ToLowListingAttempt.Split(" ").Last().Replace(",", "").Replace("!", ""));
        if (parsed > sellPrice && parsed < sellPrice * 1.1)
        {
            span.Log($"Price too low, retrying with {parsed}");
            socket.Send(Response.Create("createAuction", new
            {
                Slot = index,
                Price = parsed,
                Duration = listTime ?? 96,
                ItemName = auction.ItemName,
                Id = id
            }));
            socket.SessionInfo.ToLowListingAttempt = null;
            await Task.Delay(2000);
        }
        else
            span.Log($"Retry price outside of range {parsed} vs {sellPrice}");
    }

    private static string MapToGameTag(SaveAuction auction)
    {
        if (auction.Tag.StartsWith("PET_") && !auction.Tag.Contains("PET_ITEM") && !auction.Tag.Contains("PET_SKIN"))
            return "PET";
        if (auction.Tag.StartsWith("ABIPHONE"))
            return "ABIPHONE";
        if (auction.Tag.StartsWith("RUNE_"))
            return "RUNE";
        if (auction.Tag.StartsWith("POTION"))
            return "POTION";
        return auction.Tag;
    }

    public override void OnAuthorize(AccountInfo accountInfo)
    {
        socket.Dialog(db => db.Msg($"Your connection id is {socket.ConSpan.TraceId}, copy that if you encounter an error"));
        socket.sessionLifesycle.SessionInfo.FlipsEnabled = true;
        socket.SessionInfo.IsMacroBot = true;
        base.OnAuthorize(accountInfo);
        RequestInventory();
        socket.TryAsyncTimes(async () =>
        {
            await UpdateAhSlots(socket.ConSpan);
        }, "updateAhSlots", 1);
        if (socket.sessionLifesycle.FlipSettings.Value?.ModSettings?.AutoStartFlipper == null)
            return;
        socket.sessionLifesycle.FlipSettings.Value.ModSettings.AutoStartFlipper = true;
        socket.sessionLifesycle.FlipSettings.Value.Visibility.Seller = false;
    }

    /// <summary>
    /// Sends a bazaar order placement recommendation to the client
    /// </summary>
    /// <param name="itemTag">The item tag (e.g., "ENCHANTED_DIAMOND")</param>
    /// <param name="itemName">The display name for the item</param>
    /// <param name="isSell">True for sell orders, false for buy orders</param>
    /// <param name="price">The price per unit</param>
    /// <param name="amount">The amount to order</param>
    public void SendBazaarOrderRecommendation(string itemTag, string itemName, bool isSell, double price, int amount)
    {
        socket.Send(Response.Create("placeOrder", new
        {
            itemName = itemName,
            isSell = isSell,
            price = price,
            amount = amount
        }));
    }

    /// <summary>
    /// Recommends a bazaar sell order for an item using current bazaar prices
    /// </summary>
    /// <param name="itemTag">The item tag to sell</param>
    /// <param name="itemName">The display name</param>
    /// <param name="amount">Amount to sell (default 64)</param>
    public async Task RecommendBazaarSellOrder(string itemTag, string itemName, int amount = 64)
    {
        try
        {
            var bazaarApi = socket.GetService<Bazaar.Client.Api.IBazaarApi>();
            var priceHistory = await bazaarApi.GetHistoryGraphAsync(itemTag);
            var latestPrice = priceHistory.OrderByDescending(h => h.Timestamp).FirstOrDefault();

            if (latestPrice == null)
            {
                socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Could not fetch bazaar price for {itemName}"));
                return;
            }

            // Use sell price (what buyers pay) for sell orders
            var sellPrice = latestPrice.Sell;
            SendBazaarOrderRecommendation(itemTag, itemName, true, sellPrice, amount);

            socket.Dialog(db => db.MsgLine(
                $"{McColorCodes.GRAY}Recommending sell order: {McColorCodes.YELLOW}{amount}x {itemName} {McColorCodes.GRAY}at {McColorCodes.GREEN}{socket.FormatPrice((long)sellPrice)}{McColorCodes.GRAY} per unit",
                $"/bz {BazaarUtils.GetSearchValue(itemTag, itemName)}",
                "Click to open in bazaar"));
        }
        catch (Exception e)
        {
            socket.Error(e, $"Failed to recommend bazaar sell order for {itemTag}");
        }
    }

    private async Task<bool> ShouldSkip(Activity span, IPlayerApi apiService, SaveAuction item)
    {
        var uid = item.FlatenedNBT.FirstOrDefault(y => y.Key == "uuid" || y.Key == "uid").Value?.Split('-').Last();
        var foundInSent = socket.LastSent.Any(x => x.Auction.FlatenedNBT.FirstOrDefault(y => y.Key == "uid").Value == uid);
        if (foundInSent)
            return false;
        if (item.FlatenedNBT.ContainsKey("donated_museum"))
            return true; // sould bound
        if (item.Tag == "RUNEBOOK")
        {
            if (Random.Shared.NextDouble() < 0.03)
                socket.Dialog(db => db.MsgLine($"Found {item.ItemName} in inventory, it has to be auctioned manually, please create an auction for it"));
            return true; // rune books have to be auctioned manually
        }
        // ⬇⬇ sell able items ⬇⬇
        if (socket.SessionInfo.SellAll)
            return false;
        if (!string.IsNullOrEmpty(uid))
        {
            List<Api.Client.Model.BidResult> purchases = await GetItemPurchases(apiService, uid);
            span.Log($"Found {purchases.Count} purchases of {item.Tag} {item.Uuid}");
            if (purchases.Count == 0)
                return true; // not bought, keep existing items
            else
            {
                var flipData = await GetFlipData(purchases);
                var target = flipData.OrderBy(f => f.Timestamp).Select(f => f.TargetPrice).FirstOrDefault();
                Activity.Current?.Log($"Found {flipData.Count} from {string.Join(",", flipData.Select(f => f.FinderType))} flips for target {target}");
                Activity.Current?.Log(JsonConvert.SerializeObject(flipData.OrderBy(f => f.Timestamp).FirstOrDefault()));
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
        return await socket.GetService<ITrackerApi>().GetFlipsOfAuctionAsync(longId);
    }
    private async Task<List<Api.Client.Model.BidResult>> GetItemPurchases(IPlayerApi apiService, string uid)
    {
        //if (CheckedPurchase.GetValueOrDefault(uid) > 3)
        //    return new List<Api.Client.Model.BidResult>();
        var checkFilters = new Dictionary<string, string>() {
                { "UId", uid },
                { "EndAfter", (DateTime.UtcNow - TimeSpan.FromDays(14)).ToUnix().ToString() } };
        var purchases = await apiService.ApiPlayerPlayerUuidBidsGetAsync(socket.SessionInfo.McUuid, 0, checkFilters);
        //CheckedPurchase[uid] = CheckedPurchase.GetValueOrDefault(uid) + 1;
        return purchases;
    }

    /// <summary>
    /// Checks if an item was changed since purchase by comparing the FlatenedNBT of the purchase auction
    /// with the current inventory item.
    /// </summary>
    /// <param name="purchases">The list of purchases for this item</param>
    /// <param name="currentItem">The current state of the item in inventory</param>
    /// <returns>True if the item was changed, false otherwise</returns>
    private async Task<bool> WasItemChangedSincePurchase(List<Api.Client.Model.BidResult> purchases, SaveAuction currentItem)
    {
        var purchase = purchases.OrderByDescending(x => x.End).FirstOrDefault();
        if (purchase == null)
            return false;

        try
        {
            var purchaseAuction = await AuctionService.Instance.GetAuctionAsync(purchase.AuctionId, db => db.Include(a => a.Enchantments).Include(a => a.NbtData));
            if (purchaseAuction == null)
                return false;
            return ItemComparisonHelper.WasItemChanged(purchaseAuction, currentItem) && !currentItem.Tag.Contains("ROD");
        }
        catch (Exception ex)
        {
            socket.GetService<ILogger<FullAfVersionAdapter>>().LogWarning(ex, "Failed to check if item was changed since purchase for {itemUuid}", currentItem?.FlatenedNBT?.GetValueOrDefault("uuid"));
            return false;
        }
    }
}
