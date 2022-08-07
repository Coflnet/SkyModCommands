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
    protected override void Format(MinecraftSocket socket, DialogBuilder db, Api.Client.Model.FlipDetails f)
    {
        db.MsgLine($"{socket.formatProvider.GetRarityColor(Enum.Parse<Tier>(f.Tier, true))}{f.ItemName} {(f.Profit > 0 ? McColorCodes.GREEN : McColorCodes.RED)}Profit: {socket.formatProvider.FormatPrice(f.Profit)}",
                        $"https://sky.coflnet.com/auction/{f.OriginAuction}", $"Sold at: {f.SellTime:g}\nFound first by: {f.Finder}");
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
