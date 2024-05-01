using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Lists the cheapest upgrade path for some attribute", "attributeupgrade <item_name> <attrib2> {start_level} {end_level}")]
public class AttributeUpgradeCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        //await socket.ReguirePremPlus();
        var args = arguments.Trim('"').Split(' ');
        if (args.Length < 2)
            throw new CoflnetException("invalid_arguments", "Please provide: {item_type} {attribute_name} [start_level] [end_level]");
        var itemType = args[0].ToUpper();
        if (ItemDetails.Instance.GetItemIdForTag(itemType) == 0)
        {
            throw new CoflnetException("invalid_arguments", $"The item type {itemType} is not known, check that you entered a valid tag");
        }
        var attribName = CheapAttribCommand.MapAttribute(args[1]);
        var startLevel = 1;
        var endLevel = 10;
        if (args.Length > 2)
            startLevel = int.Parse(args[2]);
        if (args.Length > 3)
            endLevel = int.Parse(args[3]);
        var attribApi = socket.GetService<IAttributeApi>();
        var result = await attribApi.ApiAttributeCheapestItemTypeAttributeGetAsync(itemType, attribName, startLevel, endLevel);
        var auctionIds = result.SelectMany(r => r.Value?.Where(v => v != null).Select(long.Parse)).ToList();
        List<SaveAuction> auctions;
        using (var db = new HypixelContext())
        {
            auctions = await db.Auctions.Where(a => auctionIds.Contains(a.UId)).ToListAsync();
        }
        var lookup = auctions.ToDictionary(a => a.UId.ToString());
        var combined = result.Select(r => (r.Key, auctions: r.Value.Select(id => lookup.GetValueOrDefault(id)))).ToList();
        var costBelow = new Dictionary<int, long>();
        for (int i = 0; i < combined.Count; i++)
        {
            var r = combined[i];
            var cost = r.auctions.Where(a => a != null).Select(a => a.StartingBid).DefaultIfEmpty(0).Min();
            costBelow[i] = cost + (i > 0 ? costBelow[i - 1] : 0);
            Console.WriteLine($"{r.Key} {cost} {costBelow[i]}");
        }
        socket.Dialog(db => db.MsgLine($"§6{itemType} {attribName} {startLevel}-{endLevel}")
            .ForEach(combined, (db, r) =>
            {
                var totalBefore = costBelow.GetValueOrDefault(int.Parse(r.Key) - 2);
                var total = costBelow.GetValueOrDefault(int.Parse(r.Key) - 1);
                var tierSum = r.auctions.Where(a => a != null).Select(a => a.StartingBid).DefaultIfEmpty(0).Sum();
                db
                .MsgLine($"§7Lvl: {McColorCodes.AQUA}{int.Parse(r.Key) + 1} {McColorCodes.DARK_GRAY}({McColorCodes.GRAY}total {McColorCodes.YELLOW}{socket.FormatPrice(total)}{McColorCodes.DARK_GRAY})");
                if (totalBefore > total - totalBefore && total != totalBefore)
                {
                    db.MsgLine($"{McColorCodes.GREEN} directly buy this tier and save {socket.FormatPrice(totalBefore - tierSum)} for tier {int.Parse(r.Key)}");
                }
                if (r.auctions.Count() == 0)
                {
                    db.MsgLine("§cno auctions found");
                    return;
                }

                db.ForEach(r.auctions, (db, a) =>

                    db.If(() => a != null, db => db
                    .MsgLine(
                        $" {a.ItemName} §6{socket.FormatPrice(a.StartingBid)}", $"/viewauction {a.Uuid}",
                        $"{McColorCodes.AQUA}try to open {a.ItemName} in ah\n{McColorCodes.GRAY}execute command again if expired")
                    , db => db.MsgLine("§clbin not found"))
                );
            }));
    }
}
