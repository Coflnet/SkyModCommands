using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class OnlineCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendMessage(COFLNET + $"There are {McColorCodes.AQUA}{FlipperService.Instance.PremiumUserCount}{McColorCodes.GRAY} users connected to this server",
                    null, McColorCodes.GRAY + "there is more than one server");
            var count = await socket.GetService<Commands.FlipTrackingService>().ActiveFlipperCount();
            socket.SendMessage(COFLNET + $"{McColorCodes.AQUA}{count}{McColorCodes.GRAY} players clicked on a flip in the last 3 minutes.",
                    null, McColorCodes.GRAY + "across all plans (free included)");
        }
    }
}