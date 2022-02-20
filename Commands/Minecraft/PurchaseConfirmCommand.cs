using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class PurchaseConfirmCommand : PurchaseStartCommand
    {
        protected override string StatName => "pConfirm";
        protected override async Task ExecuteTrack(MinecraftSocket socket, string auctionUuid)
        {
            await socket.GetService<FlipTrackingService>().PurchaseConfirm(auctionUuid, socket.SessionInfo.McUuid);
        }
    }
}