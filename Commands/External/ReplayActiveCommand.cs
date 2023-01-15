using System.Threading.Tasks;
using Coflnet.Sky.Core;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Commands.MC;

public class ReplayActiveCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(db => db
            .MsgLine(McColorCodes.BOLD + "Replaying all active auctions against your filter...")
            .SeparatorLine());
        if (await socket.UserAccountTier() < Shared.AccountTier.PREMIUM_PLUS)
        {
            await Task.Delay(2000);
            socket.Dialog(db => db.CoflCommand<PurchaseCommand>(
                $"{McColorCodes.RED}{McColorCodes.BOLD}ABORTED\n"
                + $"{McColorCodes.RED}You need to be a premium plus user to use this command",
                "premium_plus", $"Click to purchase prem+"));
           // return; ignore this check for testing
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
                AdditionalProps = new() { { "replay", "" } }
            });
        }
        socket.Dialog(db => db
            .SeparatorLine()
            .MsgLine("All active matches are listed above")
            .SeparatorLine());
    }
}