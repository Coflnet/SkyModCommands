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
    protected override bool Hidetop3WithoutPremium => true;
    protected override void Format(MinecraftSocket socket, DialogBuilder db, NpcFlip elem)
    {
        var percentage = (elem.NpcSellPrice - elem.BuyPrice) / (double)elem.BuyPrice * 100;
        var hoverText = $"{McColorCodes.GRAY}Buy Price: {McColorCodes.GOLD}{socket.FormatPrice(elem.BuyPrice)}"
        + $"\n{McColorCodes.GRAY}Sell Price: {McColorCodes.AQUA}{socket.FormatPrice(elem.NpcSellPrice)}"
        + $"\n{McColorCodes.GRAY}Profit: {McColorCodes.GREEN}{socket.FormatPrice(elem.NpcSellPrice - elem.BuyPrice)}{McColorCodes.GRAY}";
        var click = $"/bz {elem.ItemId}";
        db.MsgLine($" {McColorCodes.GOLD}{elem.ItemName} {McColorCodes.GRAY}for {McColorCodes.GREEN}{socket.FormatPrice(percentage)}% {McColorCodes.YELLOW}[Buy]", click, hoverText);
    }

    public override async Task Execute(MinecraftSocket socket, string args)
    {
        var counter = socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdLimitsGetAsync(socket.SessionInfo.McName);
        await base.Execute(socket, args);
        var counters = (await counter).NpcSold / 10;
        socket.Dialog(db =>
        {
            db.MsgLine($"Of the 200m limit you used {McColorCodes.AQUA}{socket.FormatPrice(counters)}{McColorCodes.GRAY} so far ({counters / 200000000.0 * 100:F2}%).");
            return db;
        });
    }

    protected override async Task<IEnumerable<NpcFlip>> GetElements(MinecraftSocket socket, string val)
    {
        var npcService = socket.GetService<INpcApi>();
        return (await npcService.GetNpcFlipsAsync()).OrderByDescending(elem=>(elem.NpcSellPrice - elem.BuyPrice) / (double)elem.BuyPrice);
    }

    protected override string GetId(NpcFlip elem)
    {
        return elem.ItemId;
    }
}
