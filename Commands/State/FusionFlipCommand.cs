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
        db.MsgLine($"Combine {string.Join(" and ", elem.Inputs.Select(i => $"{McColorCodes.GOLD}{i.Key}{McColorCodes.GRAY}x{i.Value}"))} to {McColorCodes.AQUA}{elem.Output} {McColorCodes.RESET}for {McColorCodes.GOLD}{socket.FormatPrice(elem.OutputValue)}",
                $"/bz {elem.Output}",
                $"click to open the bz of {elem.Output}\n{McColorCodes.GRAY}to check the price yourself"
                + $"\n{McColorCodes.GRAY}Volume: {elem.Volume}")
            .ForEach(elem.Inputs, (db, ing) => db.MsgLine($"{McColorCodes.GRAY}- bz {McColorCodes.RESET}{ing.Key} {McColorCodes.GRAY}x{ing.Value}", "/bz " + ing.Key, "Open the bazaar to place buy order for this item \n(click)"));
    }

    protected override async Task<IEnumerable<FuseFlip>> GetElements(MinecraftSocket socket, string val)
    {
        var bazaarFlipApi = socket.GetService<IBazaarFlipperApi>();
        var fusions = await bazaarFlipApi.FusionGetAsync();

        if (await socket.UserAccountTier() == Shared.AccountTier.NONE)
            foreach (var item in fusions.Take(2))
            {
                item.Output = $"{McColorCodes.RED}requires at least starter premium";
                item.Inputs.Add("hidden", item.Inputs.First().Value);
                item.Inputs.Remove(item.Inputs.First().Key);
            }
        return fusions;
    }

    protected override string GetId(FuseFlip elem)
    {
        return elem.Output;
    }
}
