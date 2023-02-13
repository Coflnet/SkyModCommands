using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class FlipsCommand : ReadOnlyListCommand<Api.Client.Model.FlipDetails>
{
    public override bool IsPublic => true;
    public FlipsCommand()
    {
        sorters.Add("profit", e => e.OrderByDescending(f => f.Profit));
        sorters.Add("best", e => e.OrderByDescending(f => f.Profit));
        sorters.Add("time", e => e.OrderByDescending(f => f.SellTime));
        sorters.Add("recent", e => e.OrderByDescending(f => f.SellTime));
        sorters.Add("name", e => e.OrderBy(f => f.ItemName));
        sorters.Add("price", e => e.OrderByDescending(f => f.SoldFor));
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Api.Client.Model.FlipDetails f)
    {
        db.MsgLine($"{socket.formatProvider.GetRarityColor(Enum.Parse<Tier>(f.Tier, true))}{f.ItemName} {(f.Profit > 0 ? McColorCodes.GREEN : McColorCodes.RED)}Profit: {socket.formatProvider.FormatPrice(f.Profit)}",
                        $"https://sky.coflnet.com/auction/{f.OriginAuction}", $"Sold at: {f.SellTime:g}\nFound first by: {(LowPricedAuction.FinderType)f.Finder}");
    }

    protected override async Task<IEnumerable<Api.Client.Model.FlipDetails>> GetElements(MinecraftSocket socket, string val)
    {
        var response = await socket.GetService<IFlipApi>().ApiFlipStatsPlayerPlayerUuidGetAsync(socket.SessionInfo.McUuid, 7);
        return response.Flips;
    }

    protected override string GetId(Api.Client.Model.FlipDetails elem)
    {
        return elem.ItemName + elem.ItemTag + elem.PricePaid;
    }

    protected override string Title => "Your flips";
}
