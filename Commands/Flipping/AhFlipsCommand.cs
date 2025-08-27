using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
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
    protected override void Format(MinecraftSocket socket, DialogBuilder db, UnsoldFlip elem)
    {
        var converted = JsonConvert.DeserializeObject<Core.LowPricedAuction>(JsonConvert.SerializeObject(elem.Flip));
        var formatted = socket.formatProvider.FormatFlip(FlipperService.LowPriceToFlip(converted));
        db.MsgLine(formatted,
            $"/viewauction {elem.Flip.Auction.Uuid}",
            elem.Flip.Auction.Context.GetValueOrDefault("lore", $"Open {elem.Flip.Auction.ItemName} auction").ToString());
    }

    protected override async Task<IEnumerable<UnsoldFlip>> GetElements(MinecraftSocket socket, string val)
    {
        if (!await socket.RequirePremium())
            return [];
        var trackerApi = socket.GetService<ITrackerApi>();
        var unsold = await trackerApi.GetUnsoldFlipsAsync(DateTime.UtcNow.AddMinutes(-1), 30);
        return unsold;
    }

    protected override string GetId(UnsoldFlip elem)
    {
        return elem.Flip.Auction.ItemName + elem.Flip.Auction.Uuid;
    }
}
