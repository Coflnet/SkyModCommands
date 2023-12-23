using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using static Coflnet.Sky.Commands.MC.BazaarCommand;
namespace Coflnet.Sky.Commands.MC;

public class BazaarCommand : ReadOnlyListCommand<Element>
{
    public override bool IsPublic => true;
    protected override string Title => "Top Bazaar Flips";
    protected override string NoMatchText => $"No match found, maybe a typo or manipulated items are hidden";

    private static string GetSearchValue(BazaarFlip f, string name)
    {
        if (f.ItemTag.StartsWith("ENCHANTMENT_"))
        {
            // remove enchant from end
            name = name.Substring(0, name.Length - 10);
            var number = Regex.Match(f.ItemTag, @"\d+").Value;
            var converted = Roman.To(int.Parse(number));
            name = $"{name.Trim()} {converted}";
        }
        return name;
    }

    protected override async Task<IEnumerable<Element>> GetElements(MinecraftSocket socket, string val)
    {
        var api = socket.GetService<IBazaarFlipperApi>();
        var items = socket.GetService<Items.Client.Api.IItemsApi>();
        var topFlips = await api.FlipsGetAsync();
        var names = (await items.ItemNamesGetAsync()).ToDictionary(i => i.Tag, i => i.Name);
        return topFlips.Select(f =>
        {
            var profitmargin = f.BuyPrice / f.MedianBuyPrice;
            var isManipulated = profitmargin > 2 || profitmargin > 1.5 && f.BuyPrice > 7_500_000;
            return new Element
            {
                Flip = f,
                ItemName = names[f.ItemTag],
                IsManipulated = isManipulated
            };
        }).Where(f => !f.IsManipulated || !socket.Settings.Visibility.HideManipulated)
        .OrderByDescending(e => (int)e.Flip.ProfitPerHour - e.Flip.BuyPrice * 0.0125 * (int)e.Flip.Volume / 168);
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Element elem)
    {
        var userFees = 0.0125; // maybe load this in the future to be exact
        var hourlyVolume = (double)elem.Flip.Volume / 168;
        var fees = elem.Flip.BuyPrice * userFees * hourlyVolume;
        var profit = elem.Flip.ProfitPerHour - fees;
        var isManipulated = elem.IsManipulated;
        var color = isManipulated ? McColorCodes.GRAY : McColorCodes.GREEN;
        db.MsgLine($"{McColorCodes.GRAY}>{(isManipulated ? "[!]" + McColorCodes.STRIKE : McColorCodes.YELLOW)}{elem.ItemName}{McColorCodes.GRAY}: est {color}{socket.FormatPrice((long)profit)} per hour",
                $"/bz {GetSearchValue(elem.Flip, elem.ItemName)}",
                $"{(isManipulated ? McColorCodes.RED + $"Probably manipulated preceed with caution\nYou can hide manipulated items with \n{McColorCodes.AQUA}/cl s hideManipulated true\n\n" : "")}"
                + $"{McColorCodes.YELLOW}{socket.FormatPrice((long)elem.Flip.SellPrice)}->{McColorCodes.GREEN}{socket.FormatPrice((long)elem.Flip.BuyPrice)} {McColorCodes.GRAY}{socket.FormatPrice((long)fees)} fees"
                + $"\n{McColorCodes.GRAY}avg {socket.FormatPrice(hourlyVolume)} sales per hour"
                + $"\n Click to view in bazaar\n{McColorCodes.DARK_GRAY}Requires booster cookie");
    }

    protected override string GetId(Element elem)
    {
        return elem.Flip.ItemTag + elem.ItemName;
    }

    public class Element
    {
        public BazaarFlip Flip { get; set; }
        public string ItemName { get; set; }
        public bool IsManipulated { get; internal set; }
    }
}