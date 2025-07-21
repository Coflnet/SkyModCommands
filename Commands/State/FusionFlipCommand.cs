using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists flips that can be made with fusionmachine",
    "Assumes you have top buy order, fuse it and",
    "then have top sell order to sell the shard")]
public class FusionFlipCommand : ReadOnlyListCommand<FuseFlip>
{
    protected override void Format(MinecraftSocket socket, DialogBuilder db, FuseFlip elem)
    {
        db.MsgLine($"Combine {string.Join(" and ", elem.Inputs.Select(i => $"{i.Key}{McColorCodes.GRAY}x{i.Value}"))} to {McColorCodes.AQUA}{elem.Output} {McColorCodes.RESET}for {socket.FormatPrice(elem.OutputValue)}",
                $"/bz {elem.Output}",
                $"click to open the bz of {elem.Output}\n{McColorCodes.GRAY}do that before you buy the things to fuse"
                + $"\n{McColorCodes.GRAY}Volume: {elem.Volume}")
            .ForEach(elem.Inputs, (db, ing) => db.MsgLine($"{McColorCodes.GRAY}- bz {McColorCodes.RESET}{ing.Key} x{ing.Value}", "/bz " + ing.Key, "Open the bazaar to place buy order for this item \n(click)"));
    }

    protected override async Task<IEnumerable<FuseFlip>> GetElements(MinecraftSocket socket, string val)
    {
        var bazaarFlipApi = socket.GetService<IBazaarFlipperApi>();
        return await bazaarFlipApi.FusionGetAsync();
    }

    protected override string GetId(FuseFlip elem)
    {
        return elem.Output;
    }
}
