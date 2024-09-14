using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class SellProfitCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var uuid = socket.SessionInfo.McUuid;
        var filter = new Dictionary<string, string>
        {
            { "EndAfter", DateTimeOffset.UtcNow.ToUniversalTime().ToString() }
        };
        var auctionApi = socket.GetService<IAuctionsApi>();
        var auctions = new List<SaveAuction>();
        using (var context = new HypixelContext())
        {
            var profile = await context.Players.FindAsync(socket.SessionInfo.McUuid);
            auctions = await context.Auctions.Where(a => a.SellerId == profile.Id && a.End > DateTime.UtcNow).Include(a => a.NbtData).ToListAsync();
            Activity.Current.Log($"Found {JsonConvert.SerializeObject(auctions)}");
        }
        var lookup = auctions.Select(a => (a, a.FlatenedNBT.Where(n => n.Key == "uid" || n.Key == "uuid").FirstOrDefault())).Where(s => s.Item2.Value != default).ToDictionary(s => s.Item2.Value, s => s.a);
        var itemUids = lookup.Keys.Distinct().ToList();
        var previous = await auctionApi.ApiAuctionsUidsSoldPostAsync(new() { Uuids = itemUids });
        Activity.Current.Log($"Found {lookup.Count} auctions and {previous.Count} sold auctions");
        var all = new List<(SaveAuction auction, long listPrice)>();
        foreach (var item in lookup)
        {
            var auction = item.Value;
            if (!previous.TryGetValue(item.Key, out var sold))
            {
                all.Add((auction, 0));
                continue;
            }
            var purchasePrice = sold.OrderByDescending(s => s.Timestamp).First().HighestBid;
            all.Add((auction, purchasePrice));
        }
        var profit = all.Sum(a => FlipInstance.ProfitAfterFees(a.auction.StartingBid, a.listPrice));
        var unkown = all.Count(a => a.listPrice == 0);
        var top = all.OrderByDescending(a => FlipInstance.ProfitAfterFees(a.auction.StartingBid, a.listPrice)).Take(3)
            .Select(a => $"{a.auction.ItemName} {a.listPrice} -> {a.auction.StartingBid} = {FlipInstance.ProfitAfterFees(a.auction.StartingBid, a.listPrice)}").ToList();

        var hover = "Top 3 items:\n" + string.Join("\n", top)
            + $"\nUnknown auctions: {McColorCodes.AQUA}{unkown} {McColorCodes.GRAY}(no uuid/no purchase)";
        socket.Dialog(db => db
            .MsgLine($"You will make {McColorCodes.AQUA}{socket.FormatPrice(profit)} profit{McColorCodes.GRAY} from {McColorCodes.AQUA}{all.Count} autions {McColorCodes.GRAY}when every sold", null, hover));
    }
}