using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
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

    protected override async Task<bool> CanRun(MinecraftSocket socket, string args)
    {
        var trimmed = Convert<string>(args).ToLower();
        var isList = trimmed == "l" || trimmed == "list";
        if (trimmed != "h" && trimmed != "history" && !isList)
        {
            socket.Dialog(db => db.MsgLine("Check out your profit with /cl bz history", "/cofl bz history", $"Click to view your bazaar profit history\nOr run {McColorCodes.AQUA}/cofl bz h"));
            return true;
        }
        var bazaarProfitService = socket.GetService<IBazaarProfitApi>();
        if (isList)
        {
            var completedFlips = await bazaarProfitService.BazaarProfitFlipsPlayerUuidGetAsync(Guid.Parse(socket.SessionInfo.McUuid), DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 10);
            socket.Dialog(db =>
            {
                db.MsgLine($"§6Last Completed Bazaar Flips§r");
                if (completedFlips.Count == 0)
                {
                    db.MsgLine("§7No completed flips in the last 7 days§r");
                    return db;
                }
                foreach (var flip in completedFlips.AsEnumerable().Reverse())
                {
                    var profit = (double)flip.Profit/10;
                    var color = profit >= 0 ? McColorCodes.GREEN : McColorCodes.RED;
                    db.MsgLine($"{McColorCodes.GOLD}{flip.Amount}{McColorCodes.GRAY}x§7{flip.ItemName}§r: §a{socket.FormatPrice(flip.BuyPrice/10)}§r -> §a{socket.FormatPrice(flip.SellPrice/10)}§r => {color}{socket.FormatPrice(profit)}§r",
                        $"https://sky.coflnet.com/bazaar/item/{flip.ItemTag}",
                        $"Bought for {socket.FormatPrice((double)flip.BuyPrice/10)}, sold for {socket.FormatPrice((double)flip.SellPrice/10)}\n"
                        + $"Profit: {socket.FormatPrice(profit)}\n"
                        + $"Items flipped: {flip.Amount}\n"
                        + $"Completed: {socket.formatProvider.FormatTime(DateTime.UtcNow-flip.SoldAt)}n"
                        + $"Click to view {flip.ItemName} on the website");
                }
                return db;
            });
            return false;
        }

        var info = await bazaarProfitService.BazaarProfitSummaryPlayerUuidGetAsync(Guid.Parse(socket.SessionInfo.McUuid), DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 500);
        socket.Dialog(db =>
            db.MsgLine($"§6Bazaar Profit History (last 7 days)§r")
            .MsgLine($"§7Total Profit: §a{socket.FormatPrice(info.TotalProfit)}")
            .MsgLine($"§7Average Daily Profit: §a{socket.FormatPrice(info.TotalProfit / 7)}")
            .MsgLine($"§7Flips completed: §a{socket.FormatPrice(info.FlipCount)}", "/cofl bz l", "Click to view your last completed flips")
            .MsgLine($"§7Not yet sold: §a{socket.FormatPrice(info.OutstandingValue)}", null, "Not sold or not claimed\nFlips are only completed when claimed")
        );
        return false;
    }

    protected override async Task<IEnumerable<Element>> GetElements(MinecraftSocket socket, string val)
    {
        var api = socket.GetService<IBazaarFlipperApi>();
        var items = socket.GetService<Items.Client.Api.IItemsApi>();
        var purse = socket.SessionInfo.Purse <= 0 ? 1_000_000 : socket.SessionInfo.Purse;
        if (val.Length > 2) // don't limit if searching for something
            purse = 500_000_000;
        var topFlips = await api.FlipsGetAsync();
        var names = (await items.ItemNamesGetAsync()).ToDictionary(i => i.Tag, i => i.Name);
        var all = topFlips.Where(f => f.BuyPrice < purse).Select(f =>
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
                $"/bz {BazaarUtils.GetSearchValue(elem.Flip.ItemTag, elem.ItemName)}",
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