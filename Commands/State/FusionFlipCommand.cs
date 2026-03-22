using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists flips that can be made with fusionmachine",
    "Assumes you have top buy order, fuse it and",
    "then have top sell order to sell the shard",
    "use 'multistep' argument to show multi-step fusions")]
public class FusionFlipCommand : ReadOnlyListCommand<FusionFlipCommand.WithName>
{
    private static Dictionary<string, string> itemNameCache = new Dictionary<string, string>();
    protected override string NoMatchText => "Seems like there are currently no profitable fusion flips, take a look at other bazaar based flips with /cofl bazaar";
    protected override void Format(MinecraftSocket socket, DialogBuilder db, WithName elem)
    {
        if (elem.Steps != null && elem.Steps.Count > 1)
        {
            db.MsgLine($"{McColorCodes.LIGHT_PURPLE}[Multi-step x{elem.Depth}] {McColorCodes.RESET}Fuse to {McColorCodes.AQUA}{elem.ItemName} {McColorCodes.RESET}for {McColorCodes.GOLD}{socket.FormatPrice(elem.OutputValue)}",
                $"/bz {elem.ItemName}",
                $"click to open the bz of {elem.ItemName}\n{McColorCodes.GRAY}to check the price yourself"
                + $"\n{McColorCodes.GRAY}Volume: {elem.Volume}"
                + $"\n{McColorCodes.GRAY}Steps: {elem.Steps.Count}")
            .ForEach(elem.Steps, (db, step) => db.MsgLine(
                $"{McColorCodes.GRAY}  Step: {string.Join(" + ", step.Inputs.Select(i => $"{McColorCodes.GOLD}{GetName(i.Key)}{McColorCodes.GRAY}x{i.Value}"))} → {McColorCodes.AQUA}{GetName(step.Output)} {McColorCodes.GRAY}x{step.OutputCount}",
                "/bz " + GetName(step.Output),
                $"Fuse {string.Join(" + ", step.Inputs.Select(i => $"{GetName(i.Key)} x{i.Value}"))} to get {GetName(step.Output)} x{step.OutputCount}"));
        }
        else
        {
            db.MsgLine($"Combine {string.Join(" and ", elem.Inputs.Select(i => $"{McColorCodes.GOLD}{GetName(i.Key)}{McColorCodes.GRAY}x{i.Value}"))} to {McColorCodes.AQUA}{elem.ItemName} {McColorCodes.RESET}for {McColorCodes.GOLD}{socket.FormatPrice(elem.OutputValue)}",
                    $"/bz {elem.ItemName}",
                    $"click to open the bz of {elem.ItemName}\n{McColorCodes.GRAY}to check the price yourself"
                    + $"\n{McColorCodes.GRAY}Volume: {elem.Volume}")
                .ForEach(elem.Inputs, (db, ing) => db.MsgLine($"{McColorCodes.GRAY}- bz {McColorCodes.RESET}{GetName(ing.Key)} {McColorCodes.GRAY}x{ing.Value}", "/bz " + GetName(ing.Key), "Open the bazaar to place buy order for this item \n(click)"));
        }
    }

    public static string GetName(string tag)
    {
        return BazaarUtils.GetSearchValue(tag, itemNameCache.GetValueOrDefault(tag,tag));
    }

    protected override async Task<IEnumerable<WithName>> GetElements(MinecraftSocket socket, string val)
    {
        var itemNamesTask = socket.GetService<Items.Client.Api.IItemsApi>().ItemNamesGetAsync();
        var bazaarFlipApi = socket.GetService<IBazaarFlipperApi>();
        var data = await bazaarFlipApi.FusionGetWithHttpInfoAsync();
        var fusions = Newtonsoft.Json.JsonConvert.DeserializeObject<List<WithName>>(data.RawContent)
                .OrderByDescending(f => (f.OutputValue - f.InputCost) * f.Volume).ToList();
        itemNameCache = (await itemNamesTask).ToDictionary(i => i.Tag, i => i.Name);

        var showMultiStep = val?.Trim().ToLower() == "multistep";
        var hasPremium = await socket.UserAccountTier() >= Shared.AccountTier.STARTER_PREMIUM;

        if (showMultiStep)
        {
            if (!hasPremium)
            {
                socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Multi-step fusion flips require at least starter premium", null, null));
                return fusions.Take(0);
            }
            var multiData = await bazaarFlipApi.FusionMultistepGetWithHttpInfoAsync();
            var multiFusions = Newtonsoft.Json.JsonConvert.DeserializeObject<List<WithName>>(multiData.RawContent)
                    .OrderByDescending(f => (f.OutputValue - f.InputCost) * f.Volume).ToList();
            foreach (var item in multiFusions)
                item.ItemName = itemNameCache.GetValueOrDefault(item.Output, item.Output);
            return multiFusions;
        }

        foreach (var item in fusions)
        {
            item.ItemName = itemNameCache.GetValueOrDefault(item.Output, item.Output);
        }

        if (await socket.UserAccountTier() == Shared.AccountTier.NONE)
            foreach (var item in fusions.Take(2))
            {
                item.Output = $"{McColorCodes.RED}requires at least starter premium";
                item.Inputs.Add("hidden", item.Inputs.First().Value);
                item.Inputs.Remove(item.Inputs.First().Key);
            }

        // Add hint about multi-step fusions for non-premium users
        if (!hasPremium)
        {
            socket.Dialog(db => db.MsgLine(
                $"{McColorCodes.YELLOW}Multi-step fusion flips are available with starter premium! Use {McColorCodes.AQUA}/cofl fusionflip multistep",
                "/cofl premium",
                "Click to learn about premium features"));
        }

        return fusions;
    }

    protected override string GetId(WithName elem)
    {
        return elem.Output;
    }

    public class WithName : FuseFlip
    {
        public string ItemName { get; set; }

    }
}
