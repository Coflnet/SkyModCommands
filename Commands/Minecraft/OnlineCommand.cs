using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class OnlineCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendMessage(COFLNET + $"There are {McColorCodes.AQUA}{hypixel.FlipperService.Instance.PremiumUserCount}{McColorCodes.GRAY} users connected to this server");
            var count = await Commands.FlipTrackingService.Instance.ActiveFlipperCount();
            socket.SendMessage(COFLNET + $"{McColorCodes.AQUA}{count}{McColorCodes.GRAY} players clicked on a flip in the last 3 minutes.");
        }
    }
}