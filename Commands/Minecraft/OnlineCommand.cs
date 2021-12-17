using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class OnlineCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendMessage(COFLNET + $"There are {hypixel.FlipperService.Instance.PremiumUserCount} users connected to this server");
            return Task.CompletedTask;
        }
    }
}