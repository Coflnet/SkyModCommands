using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class DelayCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var delayAmount = socket.SessionInfo.Penalty;
            if (delayAmount == System.TimeSpan.Zero)
                socket.SendMessage(COFLNET + $"You are currently not delayed at all :)", null, "Enjoy flipping at full speed☻");
            else
                socket.SendMessage(COFLNET + $"You are currently delayed by {McColorCodes.AQUA}{delayAmount.TotalSeconds}s{McColorCodes.GRAY} by the fairness system. This will decrease over time.",
                        null, McColorCodes.GRAY + "Your call to this has been recorded, \nattempts to trick the system will be punished.");
        }
    }
}