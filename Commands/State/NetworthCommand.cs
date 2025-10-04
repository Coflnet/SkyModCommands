using System;
// using System.Linq; (duplicate removed)
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.PlayerState.Client.Api;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Get a breakdown of networth",
    "Based on current market prices")]
public class NetworthCommand : ArgumentsCommand
{
    public override bool IsPublic => true;

    protected override string Usage => "<username> [profile=used]";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var profile = args["profile"];
        var username = args["username"];
        var profileApi = socket.GetService<IProfileClient>();
        var pricesApi = socket.GetService<IPricesApi>();
        var accountUuid = await socket.GetPlayerUuid(username, false);
        var profileData = await profileApi.GetProfiles(accountUuid);
        var profileId = profileData.FirstOrDefault(p => p.Key.Equals(profile, System.StringComparison.InvariantCultureIgnoreCase)).Key ?? "used";
        if (profileId == "used")
        {
            profileId = (await profileApi.GetActiveProfileId(accountUuid)).Trim('"');
            Console.WriteLine($"Using active profile {profileId}");
            var mappedName = profileData.FirstOrDefault(p => p.Value.Equals(profileId));
            if (mappedName.Key != null)
            {
                profile = mappedName.Key;
            }
        }
        var after = DateTime.UtcNow.AddMinutes(-15);
        var profileInfo = await profileApi.GetProfile(accountUuid, profileId, after);
        // Build the profile payload
        var virtualFull = new Api.Client.Model.Profile()
        {
            Members = new() { { accountUuid, profileInfo } },
        };

        // Start networth API call and chest valuation in parallel to reduce latency
        var networthTask = pricesApi.ApiNetworthPostAsync(virtualFull);

        async Task<(decimal chestTotal, List<(string name, decimal value)> breakdown, List<List<Coflnet.Sky.Commands.MC.SearchCommand.ItemLocation>> topItems)> ComputeChestValuesAsync()
        {
            var chestBreakdownLocal = new List<(string name, decimal value)>();
            var chestTopItemsLocal = new List<List<Coflnet.Sky.Commands.MC.SearchCommand.ItemLocation>>();
            decimal chestTotalLocal = 0m;

            var stateApi = socket.GetService<IPlayerStateApi>();
            var sniper = socket.GetService<Shared.ISniperClient>();

            try
            {
                var allChests = await stateApi.PlayerStatePlayerIdStorageGetAsync(Guid.Parse(accountUuid), Guid.Empty);

                // Filter chests we want to value and keep order
                var chests = allChests.Where(c => c.Name != null && c.Name.Contains("Chest") && !c.Name.Contains("Ender")).ToList();
                if (chests.Count == 0)
                {
                    return (0m, chestBreakdownLocal, chestTopItemsLocal);
                }

                // Build auctions and map each chest+slot to a global auction index
                var allAuctions = new List<SaveAuction>();
                var chestIndexMap = new List<List<(int globalIndex, int slot)>>();
                foreach (var chest in chests)
                {
                    var indices = new List<(int, int)>();
                    for (int slot = 0; slot < chest.Items.Count; slot++)
                    {
                        try
                        {
                            var a = Coflnet.Sky.Commands.MC.ItemConversionHelpers.ConvertToAuction(chest.Items[slot]);
                            allAuctions.Add(a);
                            indices.Add((allAuctions.Count - 1, slot));
                        }
                        catch
                        {
                            // skip
                        }
                    }
                    chestIndexMap.Add(indices);
                }

                if (allAuctions.Count == 0)
                {
                    foreach (var chest in chests)
                    {
                        chestBreakdownLocal.Add((chest.Name ?? "inventory", 0m));
                        chestTopItemsLocal.Add(new List<Coflnet.Sky.Commands.MC.SearchCommand.ItemLocation>());
                    }
                    return (0m, chestBreakdownLocal, chestTopItemsLocal);
                }

                var prices = (await sniper.GetPrices(allAuctions.ToArray())).ToList();

                // Compute per-chest totals and top items with minimal nesting
                for (int ci = 0; ci < chests.Count; ci++)
                {
                    var chest = chests[ci];
                    var indices = chestIndexMap[ci];
                    decimal chestValue = 0m;
                    var perItemValues = new List<(decimal value, int slot)>();

                    foreach (var (globalIdx, slot) in indices)
                    {
                        if (globalIdx < 0 || globalIdx >= prices.Count) continue;
                        var p = prices[globalIdx];
                        var auction = allAuctions[globalIdx];
                        var estimated = (decimal)(p?.Median != 0 ? p.Median : p?.Lbin?.Price ?? 0);
                        var stackVal = estimated * auction.Count;
                        chestValue += stackVal;
                        perItemValues.Add((stackVal, slot));
                    }

                    var topSlots = perItemValues.OrderByDescending(x => x.value).Take(5).ToList();
                    var topLocations = new List<Coflnet.Sky.Commands.MC.SearchCommand.ItemLocation>();
                    foreach (var (value, slot) in topSlots)
                    {
                        var item = chest.Items[slot];
                        topLocations.Add(new Coflnet.Sky.Commands.MC.SearchCommand.ItemLocation
                        {
                            Chestname = chest.Name,
                            CommandToOpen = null,
                            Title = null,
                            Position = chest.Position,
                            Item = item,
                            SlotId = slot
                        });
                    }

                    chestTotalLocal += chestValue;
                    chestBreakdownLocal.Add((chest.Name ?? "inventory", chestValue));
                    chestTopItemsLocal.Add(topLocations);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not fetch/playerstate or price items: " + ex.Message);
            }

            return (chestTotalLocal, chestBreakdownLocal, chestTopItemsLocal);
        }

        var chestValuesTask = ComputeChestValuesAsync();

        // await both
        await Task.WhenAll(networthTask, chestValuesTask);
        var networth = networthTask.Result;
        var chestResult = chestValuesTask.Result;
        decimal chestTotal = chestResult.chestTotal;
        List<(string name, decimal value)> chestBreakdown = chestResult.breakdown;
        var chestTopItems = chestResult.topItems;
        var top = networth.Member.First().Value.ValuePerCategory.OrderByDescending(m => m.Value).Take(3);
        socket.Dialog(db =>
        {
            db.MsgLine($"Networth of {McColorCodes.AQUA}{username} {McColorCodes.RESET}on {McColorCodes.AQUA}{profile} {McColorCodes.RESET}is {McColorCodes.GOLD}{socket.FormatPrice(networth.FullValue)}", null, "This is currently based on api fields");
            db.ForEach(top, (db, m) => db.MsgLine($"{McColorCodes.DARK_GRAY} - {McColorCodes.RESET}{m.Key} {McColorCodes.GOLD}{socket.FormatPrice(m.Value)}"));

            // Append chest totals if available
            if (chestBreakdown.Count > 0)
            {
                db.LineBreak();
                db.MsgLine($"{McColorCodes.AQUA}Chest value total: {McColorCodes.GOLD}{socket.FormatPrice((long)chestTotal)}");
                // show up to 5 chests sorted descending
                var ordered = chestBreakdown.Select((v, idx) => (v.name, v.value, idx)).OrderByDescending(c => c.value).Take(5).ToList();
                bool first = true;
                foreach (var c in ordered)
                {
                    // If this is the most valuable chest, make it clickable and show hover with top stacks
                    var idx = c.idx;
                    if (first && chestTopItems != null && idx >= 0 && idx < chestTopItems.Count && chestTopItems[idx].Count > 0)
                    {
                        first = false;
                        var topItems = chestTopItems[idx];
                        // Build hover text
                        var hover = string.Join('\n', topItems.Select(item => $"{item.Item.ItemName} x{item.Item.Count} - {item.Item.Description}"));
                        var payload = JsonConvert.SerializeObject(topItems.First());
                        db.CoflCommand<HighlightItemCommand>($"{McColorCodes.DARK_GRAY} - {McColorCodes.RESET}{c.name} {McColorCodes.GOLD}{socket.FormatPrice((long)c.value)}", payload, hover);
                    }
                    else
                    {
                        db.MsgLine($"{McColorCodes.DARK_GRAY} - {McColorCodes.RESET}{c.name} {McColorCodes.GOLD}{socket.FormatPrice((long)c.value)}");
                    }
                }
            }

            return db;
        });

        // converted items use ItemConversionHelpers.ConvertToAuction
        await Task.Delay(10_000); // soft ratelimit
    }
}
