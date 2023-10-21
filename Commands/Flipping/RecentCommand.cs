using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Commands.MC;

public class RecentCommand : LbinCommand
{
    protected override async Task<List<SaveAuction>> GetAuctions(Dictionary<string, string> filters, int itemId, FilterEngine fe, HypixelContext context)
    {
        return await fe.AddFilters(context.Auctions
                            .Where(a => a.ItemId == itemId && a.End > DateTime.UtcNow.AddDays(-14) && a.End < DateTime.UtcNow && a.HighestBidAmount > 0), filters)
                            .OrderByDescending(a => a.End).Take(5).ToListAsync();
    }

    protected override void PrintAuctions(MinecraftSocket socket, List<SaveAuction> auctions)
    {
        var ignoreKeys = new HashSet<string>() { "uid", "uuid", "boss_tier" };
        socket.Dialog(db =>
            db.Break.ForEach(auctions.OrderBy(a => a.End), (d, a) =>
                d.MsgLine($" {socket.formatProvider.GetRarityColor(a.Tier)}{a.ItemName}{McColorCodes.GRAY}:{McColorCodes.GOLD}{a.HighestBidAmount}",
                $"https://sky.coflnet.com/a/{a.Uuid}",
                // ended since
                $"{a.End.ToString("dd.MM.yyyy HH:mm:ss")} ({socket.formatProvider.FormatTime(DateTime.UtcNow - a.End)} ago)"
                + string.Join("\n", a.Enchantments.Select(e => $"§7{e.Type}: {e.Level}"))
                + string.Join("\n", a.FlatenedNBT.Where(f => !ignoreKeys.Contains(f.Key)).Select(e => $"§7{e.Key}: {e.Value}"))
            )));
    }
}