using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class AfVersionAdapter : ModVersionAdapter
{
    protected int listSpace = 2;
    protected int activeAuctionCount = 0;
    protected Dictionary<string, int> CheckedPurchase = new();
    public AfVersionAdapter(MinecraftSocket socket) : base(socket)
    {
    }
    public override async Task<bool> SendFlip(FlipInstance flip)
    {
        _ = socket.TryAsyncTimes(TryToListAuction, "listAuction", 1);
        await SoftCapIfLimited(flip);
        (bool stopBuy, bool wait) = ShouldStopBuying();
        if (ShouldSkipFlip(flip) || stopBuy)
        {
            var reason = stopBuy ? "stopBuy" : "ShouldSkipFlip";
            socket.sessionLifesycle.FlipProcessor.BlockedFlip(flip.ToLowPriced(), reason);
            Activity.Current?.Log($"blocked by {reason}");
            return true;
        }
        var name = GetItemName(flip.Auction);
        if (flip.Auction.Count > 1)
            name = $"{McColorCodes.GRAY}{flip.Auction.Count}x {name}";
        if (wait)
        {
            var extraWait = Random.Shared.Next(0, 500);
            await Task.Delay(extraWait);
            Activity.Current.Log($"Waited {extraWait}ms");
        }
        PrintFlipCommand(flip, name);
        if (flip.IsWhitelisted())
        {
            _ = socket.TryAsyncTimes(async () =>
            {
                await FindMatchingWhitelist(flip, name);
            }, "whitelist find", 1);
        }
        Activity.Current?.SetTag("finder", flip.Finder);
        Activity.Current?.SetTag("target", flip.MedianPrice);
        Activity.Current?.SetTag("itemName", name);

        return true;


        static bool IsNotSpecialCase(FlipInstance flip)
        { // (not) do not releist or user finder, -2 and -1
            return flip.Target > 0 && flip.Profit < 20_000_000;
        }

        async Task SoftCapIfLimited(FlipInstance flip)
        {
            var minProfitPercent = socket.Settings?.MinProfitPercent ?? 0;
            if (socket.SessionInfo.AhSlotsOpen < 2)
                minProfitPercent = Math.Max(9, minProfitPercent);
            if (flip.Finder != LowPricedAuction.FinderType.USER && flip.ProfitPercentage < minProfitPercent && IsNotSpecialCase(flip))
            {
                Activity.Current?.Log($"profitpercent too low {flip.ProfitPercentage} < {minProfitPercent} | {flip.Finder} {flip.Auction.StartingBid} -> {flip.Target}");
                await Task.Delay(100);
            }
        }
    }

    private async Task FindMatchingWhitelist(FlipInstance flip, string name)
    {
        await Task.Delay(300);
        if (socket.Settings.WhiteList.Count > 30 && socket.sessionLifesycle.spamController.SentSinceReset > 3)
        {
            socket.Dialog(db => db.CoflCommand<WhichBLEntryCommand>($"{name} for {McColorCodes.AQUA}{flip.Auction.StartingBid} {McColorCodes.GREEN}matched your Whitelist{McColorCodes.WHITE}, click to see which",
                             JsonConvert.SerializeObject(new WhichBLEntryCommand.Args() { Uuid = flip.Auction.Uuid, WL = true })));
            return;
        }
        foreach (var item in socket.Settings.WhiteList)
        {
            if (!item.MatchesSettings(flip, socket.SessionInfo))
                continue;
            socket.Dialog(db => db.Msg($"{name} for {flip.Auction.StartingBid} matched your Whitelist entry: {BlacklistCommand.FormatEntry(item)}\n" +
                $"Found by {flip.Finder} finder"));
            Activity.Current.Log("Whitelisted by " + JsonConvert.SerializeObject(item));
            break;
        }
    }

    protected virtual void PrintFlipCommand(FlipInstance flip, string name)
    {
        socket.Send(Response.Create("flip", new
        {
            id = flip.Auction.Uuid,
            startingBid = flip.Auction.StartingBid,
            purchaseAt = flip.Auction.Start + TimeSpan.FromSeconds(20) - TimeSpan.FromMilliseconds(4),
            itemName = name,
            target = flip.Target,
            finder = flip.Finder,
            profitPerc = flip.ProfitPercentage,
            tag = flip.Auction.Tag,
            vol = flip.Volume,
        }));
    }

    protected static string GetItemName(SaveAuction auction)
    {
        return (auction?.Context?.GetValueOrDefault("cname") ?? auction.ItemName).Replace("§8.", "").Replace("§8-us", "");
    }

    public virtual Task TryToListAuction()
    {
        Activity.Current?.Log("listing on client");
        return Task.CompletedTask;
    }

    public override void OnAuthorize(AccountInfo accountInfo)
    {
        base.OnAuthorize(accountInfo);
        socket.TryAsyncTimes(async () =>
        {
            socket.AccountInfo.LastMacroConnect = DateTime.UtcNow;
            await socket.sessionLifesycle.AccountInfo.Update();
        }, "updating last macro connect");
    }

    public override void SendLoginPrompt(string loginLink)
    {
        socket.Dialog(db => db.MsgLine($"Please §lclick {loginLink} to login").MsgLine("Until you do you are using the free version which will make less profit and your settings won't be saved"));
    }

    private bool ShouldSkipFlip(FlipInstance flip)
    {
        var purse = socket.SessionInfo.Purse;
        var maxPercent = socket.Settings.ModSettings.MaxPercentOfPurse;
        if (purse != 0 && flip.Auction.StartingBid > purse / 3 * 2 && maxPercent == 0)
        {
            Activity.Current?.Log("not enough purse");
            socket.Dialog(db => db.Msg($"Skipped buying {flip.Auction.ItemName} for {flip.Auction.StartingBid} because you only have {purse} purse left (max 2/3 used for one flip)"));
            return true;
        }
        
        var preService = socket.GetService<IIsSold>();
        if (preService.IsSold(flip.Uuid))
        {
            Activity.Current?.Log("sold");
            if (socket.Settings.DebugMode)
                socket.Dialog(db => db.Msg($"Skipped buying {flip.Auction.ItemName} for {flip.Auction.StartingBid} because it was likely already sold"));
            return true;
        }
        return false;

    }

    protected virtual (bool skip, bool wait) ShouldStopBuying()
    {
        return (false, true);
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
