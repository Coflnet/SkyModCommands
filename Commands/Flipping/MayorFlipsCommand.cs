using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;
using static Coflnet.Sky.Commands.MC.MayorFlipsCommand;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "List of price changes based on mayor",
    "shows you the most likely price changes",
    "for when the mayor changes",
    "This is based on historical data and",
    "can not be guranteed as game updates",
    "may distort the data")]
public class MayorFlipsCommand : ReadOnlyListCommand<Element>
{
    protected override void Format(MinecraftSocket socket, DialogBuilder db, Element elem)
    {
        var hover = $"§6{elem.ItemName}§r\n" +
            $"§7Median Buy Price: §a{socket.FormatPrice(elem.MedianPrice)}\n" +
            $"§7Expecting price of §a{socket.FormatPrice(elem.ExpectedPrice)}\n" +
            (elem.NextMayor == elem.CurrentMayor ? $"based on prices after {elem.NextMayor}" : $"based on prices during {elem.NextMayor}") +
            $"\n{McColorCodes.YELLOW}Click to open on website";
        db.MsgLine($"§6{elem.ItemName}§r: §a{elem.AverageMayorMedianDiff:+0.##%;-0.##%;0%}", $"https://sky.coflnet.com/item/{elem.ItemTag}", hover);
    }

    protected override async Task<IEnumerable<Element>> GetElements(MinecraftSocket socket, string val)
    {
        var namesTask = socket.GetService<Items.Client.Api.IItemsApi>().ItemNamesGetAsync();
        var flipsData = await socket.GetService<IBazaarFlipperApi>().MayorDiffsGetWithHttpInfoAsync();
        var names = (await namesTask).ToDictionary(i => i.Tag, i => i.Name);
        var flips = JsonConvert.DeserializeObject<List<Element>>(flipsData.RawContent);
        foreach (var item in flips)
        {
            item.ItemName = names.GetValueOrDefault(item.ItemTag, item.ItemTag);
        }
        return flips.OrderByDescending(f => Math.Abs(f.AverageMayorMedianDiff))
            .Take(500);
    }

    protected override string GetId(Element elem)
    {
        return elem.ItemName;
    }

    public class Element : MayorDiffDto
    {
        public string ItemName { get; set; }
    }
}
