using System;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class DelayCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var delayAmount = socket.sessionLifesycle.CurrentDelay;
            if(socket.sessionLifesycle.AccountInfo.Value?.Tier == 0)
            {
                socket.SendMessage(COFLNET + $"You are using the {McColorCodes.WHITE} free version{DEFAULT_COLOR} and are thus delayed by multiple minutes. Click to get more info", "https://sky.coflnet.com/premium", "Please consider supporting us");
                return Task.CompletedTask;
            }
            if (delayAmount <= System.TimeSpan.Zero)
                socket.SendMessage(COFLNET + $"You are currently not delayed at all :)", null, "Enjoy flipping at full speed☻");
            else if(delayAmount == TimeSpan.FromSeconds(0.312345))
                socket.SendMessage(COFLNET + $"You are minimally delayed to compensate for your macro :)", null, ";)");
            else
                socket.SendMessage(COFLNET + $"You are currently delayed by a maximum of {McColorCodes.AQUA}{delayAmount.TotalSeconds}s{McColorCodes.GRAY} by the fairness system. This will decrease over time and is not fully applied to all flips.",
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
            return Task.CompletedTask;
        }

        private static string FormatTimeWithReason(int amount, string reason)
        {
            return $" {McColorCodes.AQUA}{amount} {McColorCodes.YELLOW}{reason}";
        }
    }
}