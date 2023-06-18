using System.Threading.Tasks;
using Coflnet.Sky.Core;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Replay all active auctions against your filter", 
    "Useful to recheck auctions that have been listed",
    "while you were offline",
    "This will take a while", 
    "to dearchive all active auctions")]
public class ReplayActiveCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(db => db
            .MsgLine(McColorCodes.BOLD + "Replaying all active auctions against your filter...", null, "this will take a while")
            .SeparatorLine());
        if (await socket.UserAccountTier() < Shared.AccountTier.PREMIUM_PLUS)
        {
            await Task.Delay(2000);
            socket.Dialog(db => db.CoflCommand<PurchaseCommand>(
                $"{McColorCodes.RED}{McColorCodes.BOLD}ABORTED\n"
                + $"{McColorCodes.RED}You need to be a premium plus user to use this command",
                "premium_plus", $"Click to purchase prem+"));
            return;
        }
        if (!socket.Settings.AllowedFinders.HasFlag(LowPricedAuction.FinderType.USER))
        {
            socket.Dialog(db => db.CoflCommand<SetCommand>(
                 $"{McColorCodes.RED}You need to enable the USER flip finder in your flip settings to use this command",
                "sniper,user", $"Click to enable sniper and user finders"));
            return;
        }
        if (!socket.Settings.WhiteList.Any())
        {
            socket.Dialog(db => db.MsgLine(
                $"{McColorCodes.RED}You need to add a whitelist to get any result"));
            return;
        }
        using var db = new HypixelContext();
        var select = db.Auctions.Where(a =>
            a.Id > db.Auctions.Max(a => a.Id) - 1_000_000
            && a.End > System.DateTime.UtcNow
            && a.HighestBidAmount == 0)
            .Include(a => a.NbtData).Include(a => a.NBTLookup).Include(a => a.Enchantments);
        foreach (var item in select)
        {
            await socket.SendFlip(new LowPricedAuction()
            {
                Auction = item,
                Finder = LowPricedAuction.FinderType.USER,
                AdditionalProps = new() { { "replay", "" } },
                TargetPrice = item.StartingBid
            });
        }
        socket.Dialog(db => db
            .SeparatorLine()
            .MsgLine("All active matches are listed above")
            .SeparatorLine());
    }
}