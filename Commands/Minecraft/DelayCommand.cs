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
            if (socket.sessionLifesycle.AccountInfo.Value?.Tier == 0)
            {
                socket.SendMessage(COFLNET + $"You are using the {McColorCodes.WHITE} free version{DEFAULT_COLOR} and are thus delayed by multiple minutes. Click to get more info", "https://sky.coflnet.com/premium", "Please consider supporting us");
                return Task.CompletedTask;
            }
            if (delayAmount <= System.TimeSpan.Zero)
                socket.SendMessage(COFLNET + $"You are currently not delayed at all :)", null, "Enjoy flipping at full speed☻");
            else if (delayAmount == TimeSpan.FromSeconds(0.312345))
                socket.SendMessage(COFLNET + $"You are minimally delayed to compensate for your macro :)", null, ";)");
            else
                socket.SendMessage(COFLNET + $"You are currently delayed by a maximum of {McColorCodes.AQUA}{delayAmount.TotalSeconds}s{McColorCodes.GRAY} by the fairness system. This will decrease over time and is not fully applied to all flips.",
                        null, McColorCodes.GRAY + "Your call to this has been recorded, \nattempts to trick the system will be punished.");
            if (delayAmount >= TimeSpan.FromSeconds(1))
            {
                socket.SendMessage(DialogBuilder.New
                    .MsgLine("One of these is probably the reason you have such a high delay:")
                    .MsgLine(FormatTimeWithReason(1, "Anti macro delay to balance flips"), null, 
                            FormatLines(
                                "This occurs if you have a low ping or use a macro.",
                                "Don't worry, everyone gets delayed the same way.", 
                                "You can reduce this by buying slower."))
                    .MsgLine(FormatTimeWithReason(2, "Default delay for new connections, removed after a few seconds"), null, "Gets removed after a few seconds, just wait a bit.")
                    .MsgLine(FormatTimeWithReason(3, "You haven't verified your minecraft account"),
                            "https://sky.coflnet.com/player/" + socket.SessionInfo.McUuid,
                            FormatLines(
                                "To verify your minecraft account, bid",
                                "the exact amount the message says on a random auction.",
                                "Alternatively click this link and then `Claim account`."))
                    .MsgLine(FormatTimeWithReason(8, "One of your accounts got blacklisted for bad behaviour"), null,
                            FormatLines(
                                "Your account was manually reviewed",
                                "and found to be actively violating the rules.",
                                "Most likely you used unfair advantages over extended periods of time.",
                                "You can't remove this delay after it has been activated"))
                    .MsgLine(FormatTimeWithReason(12, "You haven't solved the anti afk captcha"),
                            null,
                            FormatLines(
                                "Look above for the message requesting you to solve the captcha.",
                                $"If you can't find it do {McColorCodes.AQUA}/cofl captcha{McColorCodes.YELLOW} to get a new one.",
                                "The delay will be removed after you solve the captcha 10 seconds before the next api update"))
                     );
            }
            return Task.CompletedTask;
        }

        private static string FormatLines(params string[] lines)
        {
            return McColorCodes.YELLOW + string.Join("\n", lines);
        }

        private static string FormatTimeWithReason(int amount, string reason)
        {
            return $" {McColorCodes.AQUA}{amount} {McColorCodes.YELLOW}{reason}";
        }
    }
}