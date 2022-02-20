using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using static Coflnet.Sky.Commands.MC.TrackCommand;

namespace Coflnet.Sky.Commands.MC
{
    public class PurchaseStartCommand : McCommand
    {
        protected virtual string StatName => "pStart";

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var auctionUuid = JsonConvert.DeserializeObject<PurchaseAction>(arguments).AuctionId;
            var flip = socket.GetFlip(auctionUuid);
            if (flip != null && flip.Auction.Context != null)
                flip.AdditionalProps[StatName] = (DateTime.Now - flip.Auction.FindTime).ToString();
            await ExecuteTrack(socket, auctionUuid);
        }

        protected virtual async Task ExecuteTrack(MinecraftSocket socket, string auctionUuid)
        {
            await socket.GetService<FlipTrackingService>().PurchaseStart(auctionUuid, socket.SessionInfo.McUuid);
        }

        public class PurchaseAction
        {
            public string AuctionId { get; set; }
            public string ItemId { get; set; }
        }
    }
}