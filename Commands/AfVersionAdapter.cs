using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;
public class AfVersionAdapter : ModVersionAdapter
{
    protected int listSpace = 2;
    protected int activeAuctionCount = 0;
    protected Dictionary<string, int> CheckedPurchase = new();
    protected int RemainingListings => listSpace - activeAuctionCount;
    public AfVersionAdapter(MinecraftSocket socket) : base(socket)
    {
    }
    public override Task<bool> SendFlip(FlipInstance flip)
    {
        _ = socket.TryAsyncTimes(TryToListAuction, "listAuction", 1);
        if (ShouldSkipFlip(flip) || ShouldStopBuying())
            return Task.FromResult(true);
        var name = flip.Auction?.Context?.GetValueOrDefault("cname") ?? flip.Auction.ItemName;
        if (flip.Auction.Count > 1)
            name = $"{McColorCodes.GRAY}{flip.Auction.Count}x {name}";
        socket.Send(Response.Create("flip", new
        {
            id = flip.Auction.Uuid,
            startingBid = flip.Auction.StartingBid,
            purchaseAt = flip.Auction.Start + TimeSpan.FromSeconds(20) - TimeSpan.FromMilliseconds(4),
            itemName = name,
            target = flip.Target
        }));
        if (flip.IsWhitelisted())
        {
            foreach (var item in socket.Settings.WhiteList)
            {
                if (!item.MatchesSettings(flip))
                    continue;

                socket.Dialog(db => db.Msg($"{name} for {flip.Auction.StartingBid} matched your Whitelist entry: {BlacklistCommand.FormatEntry(item)}"));
                break;
            }
        }
        Activity.Current?.SetTag("finder", flip.Finder);
        Activity.Current?.SetTag("target", flip.MedianPrice);
        Activity.Current?.SetTag("itemName", name);

        return Task.FromResult(true);
    }

    public virtual Task TryToListAuction()
    {
        Activity.Current?.Log("listing on client");
        return Task.CompletedTask;
    }

    public override void SendLoginPrompt(string loginLink)
    {
        socket.Dialog(db => db.MsgLine($"Please §lclick {loginLink} to login").MsgLine("Until you do you are using the free version which will make less profit"));
    }

    private bool ShouldSkipFlip(FlipInstance flip)
    {
        var purse = socket.SessionInfo.Purse;
        if (purse != 0 && flip.Auction.StartingBid > purse / 3 * 2)
        {
            Activity.Current?.SetTag("blocked", "not enough purse");
            socket.Dialog(db => db.Msg($"Skipped buying {flip.Auction.ItemName} for {flip.Auction.StartingBid} because you only have {purse} purse left (max 2/3 used for one flip)"));
            return true;
        }
        var minProfitPercent = socket.Settings?.MinProfitPercent ?? 0;
        if (RemainingListings < 2)
            minProfitPercent = Math.Max(9, minProfitPercent);
        if (flip.Finder != LowPricedAuction.FinderType.USER && flip.ProfitPercentage < minProfitPercent)
        {
            Activity.Current?.SetTag("blocked", "profitpercent too low < " + minProfitPercent);
            return true;
        }
        var preService = socket.GetService<PreApiService>();
        if (preService.IsSold(flip.Uuid))
        {
            Activity.Current?.SetTag("blocked", "sold");
            if (socket.Settings.DebugMode)
                socket.Dialog(db => db.Msg($"Skipped buying {flip.Auction.ItemName} for {flip.Auction.StartingBid} because it was likely already sold"));
            return true;
        }
        return false;
    }

    protected virtual bool ShouldStopBuying()
    {
        return false;
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
