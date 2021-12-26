using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using hypixel;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class RateCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(arguments).Split(" ");
            var uuid = args[0];
            var finder = args[1];
            var rating = args[2];
            using var span = socket.tracer.BuildSpan("vote").WithTag("type", rating).WithTag("finder", finder).WithTag("uuid", uuid).AsChildOf(socket.ConSpan).StartActive();
            var bad = socket.GetFlip(uuid);
            span.Span.Log(JSON.Stringify(bad));
            span.Span.Log(JSON.Stringify(bad?.AdditionalProps));


            if (rating == "down")
            {
                if(bad != null)
                Blacklist(socket, bad);
                socket.SendMessage(new ChatPart(COFLNET + "Thanks for your feedback, Please help us better understand why this flip is bad\n", null, "you can also send free text with /cofl report"),
                    new ChatPart(" * Its overpriced\n", "/cofl report overpriced "),
                    new ChatPart(" * This item sells slowly\n", "/cofl report slow sell"),
                    new ChatPart(" * I blacklisted this before\n", "/cofl report blacklist broken"),
                    new ChatPart(" * This item is manipulated\n", "/cofl report manipulated item"),
                    new ChatPart(" * Reference auctions are wrong \n", "/cofl report reference auctions are wrong ", "please send /cofl report with further information"));
                await FlipTrackingService.Instance.DownVote(uuid, socket.McUuid);
            }
            else if (rating == "up")
            {
                socket.SendMessage(new ChatPart(COFLNET + "Thanks for your feedback, Please help us better understand why this flip is good\n"),
                                    new ChatPart(" * it isn't I mis-clicked \n", "/cofl report misclicked "),
                                    new ChatPart(" * This item sells fast\n", "/cofl report fast sell"),
                                    new ChatPart(" * High profit\n", "/cofl report high profit"),
                                    new ChatPart(" * Something else \n", null, "please send /cofl report with further information"));
                await FlipTrackingService.Instance.UpVote(uuid, socket.McUuid);
            }
            else
            {
                socket.SendMessage(COFLNET + $"Thanks for your feedback, you voted this flip " + rating, "/cofl undo", "We will try to improve the flips accordingly");
            }
            await Task.Delay(3000);
            var based = await hypixel.CoreServer.ExecuteCommandWithCache<string, IEnumerable<BasedOnCommandResponse>>("flipBased", uuid);
            span.Span.Log(string.Join('\n', based?.Select(b => $"{b.ItemName} {b.highestBid} {b.uuid}")));
        }

        private static void Blacklist(MinecraftSocket socket, LowPricedAuction bad)
        {
            if (socket.Settings.BlackList == null)
                socket.Settings.BlackList = new System.Collections.Generic.List<ListEntry>();
            socket.Settings.BlackList.Add(new ListEntry() { ItemTag = bad.Auction.Tag });
        }
    }


}