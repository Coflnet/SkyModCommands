using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Commands.MC;

public class AttributeUpgradeCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        if (args.Length < 2)
            throw new CoflnetException("invalid_arguments", "Please provide: {item_type} {attribute_name} [start_level] [end_level]");
        var itemType = args[0];
        var attribName = CheapAttribCommand.MapAttribute(args[1]);
        var startLevel = 0;
        var endLevel = 10;
        if (args.Length > 2)
            startLevel = int.Parse(args[2]);
        if (args.Length > 3)
            endLevel = int.Parse(args[3]);
        var attribApi = socket.GetService<IAttributeApi>();
        var result = await attribApi.ApiAttributeCheapestItemTypeAttributeGetAsync(itemType, attribName, startLevel, endLevel);
        var auctionIds = result.Select(e => ((string level, List<string> ids))e).SelectMany(r => r.ids.Select(long.Parse)).ToList();
        List<SaveAuction> auctions;
        using (var db = new HypixelContext())
        {
            auctions = await db.Auctions.Where(a => auctionIds.Contains(a.UId)).ToListAsync();
        }
        var lookup = auctions.ToDictionary(a => a.UId.ToString());
        var combined = result.Select(e => ((string level, List<string> ids))e).Select(r => (r.level, auctions: r.ids.Select(id => lookup[id])));
        socket.Dialog(db => db.MsgLine($"§6{itemType} {attribName} {startLevel}-{endLevel}")
            .ForEach(combined, (db, r) => db
                .MsgLine($"§7Lvl: {McColorCodes.AQUA}{r.level}")
                .ForEach(r.auctions, (db, a) => db
                    .Msg(
                        $"§6{socket.FormatPrice(a.HighestBidAmount)}", $"/viewauction {a.Uuid}",
                        $"{McColorCodes.AQUA}try to open {a.ItemName} in ah\n{McColorCodes.GRAY}execute command again if expired")
                    .LineBreak())));
    }
}
