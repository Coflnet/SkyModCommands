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
                if (bad != null)
                    Blacklist(socket, bad);
                socket.SendMessage(new ChatPart(COFLNET + "Thanks for your feedback, Please help us better understand why this flip is bad\n", null, "you can also send free text with /cofl report"),
                    new ChatPart(" * it isn't I mis-clicked \n", "/cofl dialog echo okay, have a nice day "),
                    new ChatPart(" * This flip is overpriced\n", "/cofl dialog overpriced ", "overpriced/bad flip"),
                    new ChatPart(" * This item sells slowly\n", "/cofl dialog slowsell"),
                    new ChatPart(" * I blacklisted this before\n", "/cofl report blacklist broken"));
                await FlipTrackingService.Instance.DownVote(uuid, socket.McUuid);
            }
            else if (rating == "up")
            {
                socket.SendMessage(new ChatPart(COFLNET + "Thanks for your feedback, Please help us better understand why this flip is good\n"),
                                    new ChatPart(" * it isn't I mis-clicked \n", "/cofl dialog echo okay, have a nice day "),
                                    new ChatPart(" * This item sells fast\n", "/cofl report fast sell"),
                                    new ChatPart(" * High profit\n", "/cofl dialog echo Okay, sounds great, have fun with your coins :)"),
                                    new ChatPart(" * Something else \n", null, "please send /cofl report with further information"));
                await FlipTrackingService.Instance.UpVote(uuid, socket.McUuid);
            }
            else
            {
                socket.SendMessage(COFLNET + $"Thanks for your feedback, you voted this flip " + rating, "/cofl undo", "We will try to improve the flips accordingly");
            }
            await Task.Delay(3000);
            var based = await hypixel.CoreServer.ExecuteCommandWithCache<string, IEnumerable<BasedOnCommandResponse>>("flipBased", uuid);
            if (based == null)
                span.Span.Log("based not available");
            else
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