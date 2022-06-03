using System;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

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
            if (delayAmount >= TimeSpan.FromSeconds(1))
            {
                socket.SendMessage(DialogBuilder.New
                    .MsgLine("One of these is probably the reason you have such a high delay:")
                    .MsgLine(FormatTimeWithReason(1, "Anti macro delay to balance flips"))
                    .MsgLine(FormatTimeWithReason(2, "Default delay for new connections, removed after a few seconds"))
                    .MsgLine(FormatTimeWithReason(3, "You haven't verified your minecraft account"))
                    .MsgLine(FormatTimeWithReason(8, "One of your accounts got blacklisted for bad behaviour"))
                    .MsgLine(FormatTimeWithReason(12, "You haven't solved the anti afk captcha"))
                     );
            }
        }

        private static string FormatTimeWithReason(int amount, string reason)
        {
            return $" {McColorCodes.AQUA}{amount} {McColorCodes.YELLOW}{reason}";
        }
    }
}