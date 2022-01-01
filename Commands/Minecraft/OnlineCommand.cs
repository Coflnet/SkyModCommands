using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class OnlineCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var count = await Commands.FlipTrackingService.Instance.ActiveFlipperCount();
            socket.SendMessage(COFLNET + $"There are {hypixel.FlipperService.Instance.PremiumUserCount} users connected to this server\n"
                            + $"{McColorCodes.AQUA}{count}{McColorCodes.GRAY} players clicked on a flip in the last 3 minutes.");
        }
    }
}