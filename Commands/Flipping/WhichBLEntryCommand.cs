using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using System.Diagnostics;

namespace Coflnet.Sky.Commands.MC
{
    public class WhichBLEntryCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = Convert<Args>(arguments);
            var flip = socket.GetFlip(args.Uuid);
            if (flip == null)
                flip = socket.TopBlocked.Where(b => b.Flip.Auction.Uuid == args.Uuid).Select(b => b.Flip).FirstOrDefault();
            if (flip == null)
            {
                socket.SendMessage(COFLNET + "Sorry this flip wasn't found in the recently sent list on your connection, can't determine which filter it matched");
                return Task.CompletedTask;
            }
            var targetList = socket.Settings.GetForceBlacklist().Concat(socket.Settings.BlackList);
            if (args.WL)
                targetList = socket.Settings.WhiteList;

            foreach (var item in targetList)
            {
                if (Matches(socket.SessionInfo, flip, item))
                {
                    var bl = BlacklistCommand.FormatEntry(item);
                    var text = $"This flip matched the filter {bl} {McColorCodes.GRAY}[{McColorCodes.RED}REMOVE{McColorCodes.GRAY}]";
                    var isWhitelist = args.WL;
                    SendRemoveMessage(socket, item, text, isWhitelist);
                    Activity.Current.Log(JSON.Stringify(bl));
                    return Task.CompletedTask;
                }
            }
            socket.Settings.ClearListMatchers();
            socket.SendMessage(COFLNET + "This flip didn't match any of your current filters. Most likely it matched an already removed entry or one with a bed filter", null, "Reloaded the filter in an attempt to fix this");
            return Task.CompletedTask;
        }

        public static void SendRemoveMessage(MinecraftSocket socket, ListEntry item, string text, bool isWhitelist)
        {
            if (isWhitelist)
                socket.Dialog(db => db.CoflCommand<WhitelistCommand>(text, "rm " + BlacklistCommand.FormatId(item), "Remove this filter"));
            else
                socket.Dialog(db => db.CoflCommand<BlacklistCommand>(text, "rm " + BlacklistCommand.FormatId(item), "Remove this filter"));
        }

        public static bool Matches(SessionInfo info, LowPricedAuction flip, ListEntry item)
        {
            return item.MatchesSettings(FlipperService.LowPriceToFlip(flip), info);
        }

        public static ChatPart CreatePart(string text, Args target, string hover = null)
        {
            return new ChatPart(text, "/cofl whichblentry " + JSON.Stringify(target), hover);
        }

        public class Args
        {
            public string Uuid;
            public bool WL;
        }
    }

}