using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.FlipTracker.Client.Api;
using Coflnet.Sky.FlipTracker.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Shows not yet sold auction house flips",
    "Flips might still be unavailable due",
    "to update lag, just try the next one",
    "This command eases the compatitive ",
    "nature of ah flipping")]
public class AhFlipsCommand : ReadOnlyListCommand<UnsoldFlip>
{
    protected override string Title => "(probably) unsold flips";
    protected override int PageSize => 8;
    protected override void Format(MinecraftSocket socket, DialogBuilder db, UnsoldFlip elem)
    {
        var converted = System.Text.Json.JsonSerializer.Deserialize<Core.LowPricedAuction>(System.Text.Json.JsonSerializer.Serialize(elem.Flip));
        converted.Auction.FlatenedNBT = elem.Flip.Auction.FlatNbt;
        var formatted = socket.formatProvider.FormatFlip(FlipperService.LowPriceToFlip(converted));
        db.MsgLine(formatted,
            $"/viewauction {elem.Flip.Auction.Uuid}",
            elem.Flip.Auction.Context.GetValueOrDefault("lore", $"Open {elem.Flip.Auction.ItemName} auction").ToString());
    }

    protected override async Task<IEnumerable<UnsoldFlip>> GetElements(MinecraftSocket socket, string val)
    {
        if (!await socket.RequirePremium())
            return [];
        socket.Dialog(db => db.MsgLine("Chcking recently found flips if they are known to be sold", null, "Auctions might still be gone by the time you click on them"));
        var trackerApi = socket.GetService<ITrackerApi>();
        var unsold = await trackerApi.GetUnsoldFlipsAsync(DateTime.UtcNow.AddMinutes(-1.2), 50);
        var uids = unsold.Select(f => f.Uid).ToArray();
        using var db = new HypixelContext();
        var known = db.Auctions.Where(f => uids.Contains(f.UId) && f.End < DateTime.UtcNow).Select(f => f.UId).ToHashSet();
        unsold = unsold.Where(f => !known.Contains(f.Uid)).ToList();
        return unsold.OrderByDescending(f => (f.Flip.TargetPrice - f.Flip.Auction.StartingBid) * f.Flip.DailyVolume)
            .Where(f => f.Flip.Auction.StartingBid < socket.SessionInfo.Purse || socket.SessionInfo.Purse <= 0).ToList();
    }

    protected override string GetId(UnsoldFlip elem)
    {
        return elem.Flip.Auction.ItemName + elem.Flip.Auction.Uuid;
    }
}
