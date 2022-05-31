using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class WhichBLEntryCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = JsonConvert.DeserializeObject<Args>(arguments.Trim('"'));
            var flip = socket.GetFlip(args.Uuid);
            if(flip == null)
                flip = socket.TopBlocked.Where(b=>b.Flip.Auction.Uuid == args.Uuid).Select(b=>b.Flip).FirstOrDefault();
            if (flip == null)
            {
                socket.SendMessage(COFLNET + "Sorry this flip wasn't found in the recently sent list on your connection, can't determine which filter it matched");
                return Task.CompletedTask;
            }
            var targetList = socket.Settings.BlackList;
            if (args.WL)
                targetList = socket.Settings.WhiteList;

            foreach (var item in targetList)
            {
                if (item.GetExpression().Compile().Invoke(FlipperService.LowPriceToFlip(flip)))
                {
                    var bl = BlacklistCommand.FormatEntry(item);
                    socket.SendMessage(COFLNET + "This flip matched the filter " + bl);
                    return Task.CompletedTask;
                }
            }

            return Task.CompletedTask;
        }

        public class Args
        {
            public string Uuid;
            public bool WL;
        }
    }

}