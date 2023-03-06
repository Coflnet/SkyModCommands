using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class FlipsCommand : ReadOnlyListCommand<Shared.FlipDetails>
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

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Shared.FlipDetails f)
    {
        db.MsgLine($"{socket.formatProvider.GetRarityColor(Enum.Parse<Tier>(f.Tier, true))}{f.ItemName} {(f.Profit > 0 ? McColorCodes.GREEN : McColorCodes.RED)}Profit: {socket.formatProvider.FormatPrice(f.Profit)}",
                        $"https://sky.coflnet.com/auction/{f.OriginAuction}", $"Sold at: {f.SellTime:g}\nFound first by: {(LowPricedAuction.FinderType)f.Finder}");
    }

    protected override async Task<IEnumerable<Shared.FlipDetails>> GetElements(MinecraftSocket socket, string val)
    {
        // [sort] [days] [page]
        var argCount = val.Split(' ').Length;
        var dasys = val.Split(' ').Skip((argCount) / 2).FirstOrDefault() ?? "7";
        if (int.TryParse(dasys, out int days))
        {
            val = val.Substring(0, val.Length - dasys.Length);
        }
        else
        {
            throw new CoflnetException("invalid_days", "Format is: /flips [sort] [days] [page]");
        }
        if (days > 14 && await socket.UserAccountTier() < Shared.AccountTier.PREMIUM_PLUS)
            throw new CoflnetException("not_allowed", "You need to be premium plus to see more than 14 days of flips");
        var accounts = await socket.sessionLifesycle.GetMinecraftAccountUuids();
        var response = await socket.GetService<FlipTrackingService>().GetPlayerFlips(accounts, TimeSpan.FromDays(days));
        return response.Flips;
    }

    protected override string RemoveSortArgument(string arguments)
    {
        var args = base.RemoveSortArgument(arguments);
        if (args.Split(' ').Length == 1)
            return "";
        args = args.Split(' ').Skip(1).Aggregate((a, b) => a + " " + b);
        return args;
    }

    protected override string GetTitle(string arguments)
    {
        return "Your flips for the last " + arguments.Split(' ').Where(p=>int.TryParse(p, out _)).FirstOrDefault() + " days";
    }

    protected override string GetId(Shared.FlipDetails elem)
    {
        return elem.ItemName + elem.ItemTag + elem.PricePaid;
    }
}
