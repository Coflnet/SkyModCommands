using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class FactCommand : McCommand
    {
        List<Func<MinecraftSocket, string>> facts = new() {
            s=>"You are reading this right now",
            s=>"1 + 1 is 2",
            s=>"Your max cost is " + s.Settings.MaxCost,
            s=>"You use the flip finders " + s.Settings.AllowedFinders.ToString(),
            s=>$"Your whitelist has {s.Settings.WhiteList.Count} entries",
            s=>$"Your blacklist has {s.Settings.BlackList.Count} entries",
            s=> s.LastSent.Count > 0 ? $"The last flip message was for {s.LastSent.LastOrDefault()?.Auction.ItemName}" : "You got no flip since you reconnected",
            s=> "There is a /cofl reminder command",
            s=> $"You can use /cofl s or /cofl set to change settings",
            s=> $"You should use /cofl backup from time to time",
            s=> $"We are happy about you using our software :)",
            s=> $"Skyblock takes a lot of time",
            s=> $"Rule 1 is: Be nice",
            s=> $"Rule 2 is: Don't advertise something nobody asked for",
            s=> $"There aren't a lot of cofl rules",
            s=> $"Do a barrel roll!"
        };
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            throw new System.NotImplementedException();
        }
    }
}