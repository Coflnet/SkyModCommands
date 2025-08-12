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

[CommandDescription(
    "A list of the top bazaar flips",
    "Allows you to see the most profitable",
    "bazaar flips currently available",
    "It assumes that you make buy and sellers",
    "and includes the §b1.25% bazaar fee",
    "from the free bazaar community upgrade")]
public class BazaarCommand : ReadOnlyListCommand<Element>
{
    public override bool IsPublic => true;
    protected override string Title => "Top Bazaar Flips";
    protected override string NoMatchText => $"No match found, maybe a typo or manipulated items are hidden";

    public static string GetSearchValue(string tag, string name)
    {
        if (tag.StartsWith("ENCHANTMENT_"))
        {
            // remove enchant from end
            name = name.Substring(0, name.Length - 10);
            var number = Regex.Match(tag, @"\d+").Value;
            var converted = Roman.To(int.Parse(number));
            name = $"{name.Trim()} {converted}";
        }
        if (name.EndsWith("Shard"))
            name = name.Replace(" Shard", "");
        if (tag.StartsWith("ENCHANTMENT_ULTIMATE"))
        {
            name = name.Replace("ultimate ", "", StringComparison.OrdinalIgnoreCase);
        }
        return name;
    }

    protected override async Task<IEnumerable<Element>> GetElements(MinecraftSocket socket, string val)
    {
        var api = socket.GetService<IBazaarFlipperApi>();
        var items = socket.GetService<Items.Client.Api.IItemsApi>();
        var purse = socket.SessionInfo.Purse <= 0 ? 1_000_000 : socket.SessionInfo.Purse;
        var topFlips = await api.FlipsGetAsync();
        var names = (await items.ItemNamesGetAsync()).ToDictionary(i => i.Tag, i => i.Name);
        var all = topFlips.Where(f=>f.BuyPrice < purse).Select(f =>
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
            .OrderByDescending(e => (int)e.Flip.ProfitPerHour - e.Flip.BuyPrice * 0.0125 * (int)e.Flip.Volume / 168)
            .ToList();
        if (await socket.UserAccountTier() == Shared.AccountTier.NONE)
            foreach (var item in all.Take(2))
            {
                item.ItemName = $"{McColorCodes.RED}requires at least starter premium";
            }
        return all;
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Element elem)
    {
        var hourlyVolume = (double)elem.Flip.Volume / 168;
        // its assumed the user has the free bazaar upgrade, maybe load this in the future to be exact
        var fees = elem.Flip.EstimatedFees;
        var profit = elem.Flip.ProfitPerHour;
        var isManipulated = elem.IsManipulated;
        var color = isManipulated ? McColorCodes.GRAY : McColorCodes.GREEN;
        db.MsgLine($"{McColorCodes.GRAY}>{(isManipulated ? "[!]" + McColorCodes.STRIKE : McColorCodes.YELLOW)}{elem.ItemName}{McColorCodes.GRAY}: est {color}{socket.FormatPrice((long)profit)} per hour",
                $"/bz {GetSearchValue(elem.Flip.ItemTag, elem.ItemName)}",
                $"{(isManipulated ? McColorCodes.RED + $"Probably manipulated preceed with caution\nYou can hide manipulated items with \n{McColorCodes.AQUA}/cl s hideManipulated true\n\n" : "")}"
                + $"{McColorCodes.YELLOW}{socket.FormatPrice((long)elem.Flip.SellPrice)}->{McColorCodes.GREEN}{socket.FormatPrice((long)elem.Flip.BuyPrice)} {McColorCodes.GRAY}incl. {socket.FormatPrice((long)fees)} fees"
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