using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Displays craft flips you can do.",
    "Based on unlocked collectionsAnd slayer level")]
public class NpcCommand : ReadOnlyListCommand<Crafts.Client.Model.NpcFlip>
{
    protected override string Title => $"Best NPC Flips";
    protected override int PageSize => 10;
    protected override int HidetopWithoutPremium => 6;
    private HashSet<string> OnBazaar = new HashSet<string>();
    protected override void Format(MinecraftSocket socket, DialogBuilder db, NpcFlip elem)
    {
        var percentage = (elem.NpcSellPrice - elem.BuyPrice) / (double)elem.BuyPrice * 100;
        var hourlyProfit = (elem.NpcSellPrice - elem.BuyPrice) * elem.HourlySells;
        var hoverText = $"{McColorCodes.GRAY}Buy Price: {McColorCodes.GOLD}{socket.FormatPrice(elem.BuyPrice)}"
        + $"\n{McColorCodes.GRAY}Sell Price: {McColorCodes.AQUA}{socket.FormatPrice(elem.NpcSellPrice)}"
        + $"\n{McColorCodes.GRAY}Sells/Hour: {McColorCodes.AQUA}{socket.FormatPrice(elem.HourlySells)}"
        + $"\n{McColorCodes.GRAY}Profit: {McColorCodes.GREEN}{socket.FormatPrice(elem.NpcSellPrice - elem.BuyPrice)} {McColorCodes.GRAY}({percentage:F2}%)";
        var click = OnBazaar.Contains(elem.ItemId) ? $"/bz {elem.ItemName}" : $"/ahs {elem.ItemName}";
        db.MsgLine($" {McColorCodes.GOLD}{elem.ItemName} {McColorCodes.GRAY}for {McColorCodes.GREEN}{socket.FormatPrice(hourlyProfit)}/h {McColorCodes.YELLOW}[Buy]", click, hoverText);
    }

    public override async Task Execute(MinecraftSocket socket, string args)
    {
        if (OnBazaar.Count == 0)
        {
            var itemListTask = socket.GetService<Items.Client.Api.IItemsApi>().ItemsGetAsync();
            foreach (var item in await itemListTask)
            {
                if (item.Flags.Value.HasFlag(Items.Client.Model.ItemFlags.BAZAAR))
                    OnBazaar.Add(item.Tag);
            }
        }
        var counter = socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdLimitsGetAsync(socket.SessionInfo.McName);
        await base.Execute(socket, args);
        var counters = (await counter).NpcSold / 10;
        const double limit = 500000000.0; // 500m daily NPC sell earning limit
        var percent = counters / limit * 100.0;

        // Next reset is at 00:00 UTC (12:00 AM GMT). Show absolute info and relative time.
        var nowUtc = DateTime.UtcNow;
        var nextResetUtc = nowUtc.Date.AddDays(1);
        var until = nextResetUtc - nowUtc;
        var hours = (int)until.TotalHours;
        var minutes = until.Minutes;
        var untilStr = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";

        socket.Dialog(db =>
        {
            db.RemovePrefix().MsgLine($"Of the {limit / 1000000.0:0}m limit you used {McColorCodes.AQUA}{socket.FormatPrice(counters)}{McColorCodes.GRAY} so far ({percent:F2}%).", null,
            $"Resets at 12:00 AM GMT (7:00 PM EST / 8:00 PM EDT).\nNext reset in {McColorCodes.AQUA}{untilStr}{McColorCodes.GRAY}.");
            return db;
        });
    }

    protected override async Task<IEnumerable<NpcFlip>> GetElements(MinecraftSocket socket, string val)
    {
        var npcService = socket.GetService<INpcApi>();
        return (await npcService.GetNpcFlipsAsync()).OrderByDescending(elem => (elem.NpcSellPrice - elem.BuyPrice) * elem.HourlySells);
    }

    protected override string GetId(NpcFlip elem)
    {
        return elem.ItemId;
    }
}
