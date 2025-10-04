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

        var chestValuesTask = Task.Run(async () =>
        {
            decimal chestTotalLocal = 0m;
            var chestBreakdownLocal = new List<(string name, decimal value)>();
            try
            {
                var stateApi = socket.GetService<IPlayerStateApi>();
                var allChests = await stateApi.PlayerStatePlayerIdStorageGetAsync(Guid.Parse(accountUuid), Guid.Empty);
                var sniper = socket.GetService<Shared.ISniperClient>();

                // Collect all auctions into one list and remember offsets per chest
                var allAuctions = new List<SaveAuction>();
                var chestOffsets = new List<(string name, int start, int count)>();
                foreach (var chest in allChests)
                {
                    if(chest.Name == null || !chest.Name.Contains("Chest") || chest.Name.Contains("Ender"))
                        continue; // only count actual chests not already in profile api
                    int start = allAuctions.Count;
                    int added = 0;
                    foreach (var item in chest.Items)
                    {
                        try
                        {
                            var a = Coflnet.Sky.Commands.MC.ItemConversionHelpers.ConvertToAuction(item);
                            allAuctions.Add(a);
                            added++;
                        }
                        catch
                        {
                            // skip items that can't be converted
                        }
                    }
                    chestOffsets.Add((chest.Name ?? "inventory", start, added));
                }

                if (allAuctions.Count == 0)
                {
                    // nothing to price
                    foreach (var c in chestOffsets)
                        chestBreakdownLocal.Add((c.name, 0m));
                }
                else
                {
                    // Single call for all auctions. The sniper returns results in the same order and may include empty entries.
                    var prices = (await sniper.GetPrices(allAuctions.ToArray())).ToList();

                    // Map prices back to chests using offsets
                    for (int ci = 0; ci < chestOffsets.Count; ci++)
                    {
                        var (name, start, count) = chestOffsets[ci];
                        decimal chestValue = 0m;
                        for (int i = 0; i < count; i++)
                        {
                            int idx = start + i;
                            if (idx < 0 || idx >= prices.Count) continue;
                            var p = prices[idx];
                            var auction = allAuctions[idx];
                            var estimated = (decimal)(p?.Median != 0 ? p.Median : p?.Lbin?.Price ?? 0);
                            chestValue += estimated * auction.Count;
                        }
                        chestTotalLocal += chestValue;
                        chestBreakdownLocal.Add((name, chestValue));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not fetch/playerstate or price items: " + ex.Message);
            }

            return (chestTotalLocal: chestTotalLocal, chestBreakdownLocal: chestBreakdownLocal as List<(string, decimal)>);
        });

        // await both
        await Task.WhenAll(networthTask, chestValuesTask);
        var networth = networthTask.Result;
        var chestResult = chestValuesTask.Result;
        decimal chestTotal = chestResult.chestTotalLocal;
        List<(string name, decimal value)> chestBreakdown = chestResult.chestBreakdownLocal;
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
                foreach (var c in chestBreakdown.OrderByDescending(c => c.value).Take(5))
                {
                    db.MsgLine($"{McColorCodes.DARK_GRAY} - {McColorCodes.RESET}{c.name} {McColorCodes.GOLD}{socket.FormatPrice((long)c.value)}");
                }
            }

            return db;
        });

        // converted items use ItemConversionHelpers.ConvertToAuction
        await Task.Delay(10_000); // soft ratelimit
    }
}
