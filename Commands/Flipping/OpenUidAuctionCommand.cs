using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;

public class OpenUidAuctionCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var uid = long.Parse(arguments.Trim('"'));
        using (var db = new HypixelContext())
        {
            var auction = db.Auctions.Where(a => a.UId == uid).FirstOrDefault();
            if (auction == null)
                throw new CoflnetException("not_found", "The auction with the given uid was not found");
            if (auction.End < DateTime.UtcNow)
                throw new CoflnetException("expired", "The auction has already ended");
            socket.ExecuteCommand("/viewauction " + auction.Uuid);
        }
    }
}