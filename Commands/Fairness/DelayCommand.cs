using System;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class DelayCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var delayAmount = socket.sessionLifesycle.CurrentDelay;
            if (await socket.UserAccountTier() == 0)
            {
                socket.Dialog(db => db.MsgLine($"You are using the {McColorCodes.YELLOW}free version{DEFAULT_COLOR} and are thus delayed by over a minute.", "https://sky.coflnet.com/premium", "Opens the premium page")
                            .MsgLine($"Purchase premium to remove delay. {McColorCodes.AQUA}/cofl buy", "/cofl buy", "Shows the premium options"));
                return;
            }
            if (!socket.SessionInfo.FlipsEnabled)
                socket.Dialog(db => db.CoflCommand<FlipCommand>("You don't have flips enabled.\nClick to toggle flips", "", "Click to toggle them"));

            if (delayAmount <= System.TimeSpan.Zero)
                socket.SendMessage(COFLNET + $"You are currently not delayed at all :)", null, "Enjoy flipping at full speed☻");
            else if (delayAmount == TimeSpan.FromSeconds(12))
                socket.Dialog(db=>db.CoflCommand<CaptchaCommand>($"You flipped for too long and have to solve a captcha to remove your 12 second delay {McColorCodes.AQUA}click to get one", "", "Generates a new captcha"));
            else
                socket.SendMessage(COFLNET + $"You are currently delayed by a maximum of {McColorCodes.AQUA}{delayAmount.TotalSeconds}s{McColorCodes.GRAY} by the fairness system. This will decrease over time and is not fully applied to all flips.",
                        null, McColorCodes.GRAY + "Your call to this has been recorded, \nattempts to trick the system will be punished.");
            if (delayAmount >= TimeSpan.FromSeconds(1))
            {
                socket.SendMessage(GetSupportText(socket, delayAmount)
                     );
            }
        }

        private static DialogBuilder GetSupportText(MinecraftSocket socket, TimeSpan delayAmount)
        => DialogBuilder.New
            .MsgLine("The cause for your delay could be (hover for info):")
            .If(() => delayAmount < TimeSpan.FromSeconds(2), db => 
                db.MsgLine(FormatTimeWithReason(1, "Fairness delay to balance flips"), null,
                    FormatLines(
                        "This occurs if you have a low ping or use a macro.",
                        "Don't worry, everyone gets delayed the same way.",
                        "You can reduce this by buying slower.")))
            .If(() => delayAmount == TimeSpan.FromSeconds(2), db => db
                .MsgLine(FormatTimeWithReason(2, "Default delay for new connections, removed after a few seconds"), null, 
                    FormatLines(
                        "Gets removed after a few seconds, just wait a bit.",
                        "This delay exists to prevent spamming the server with new connections")))
            .If(() => delayAmount == TimeSpan.FromSeconds(3), db => db
                .MsgLine(FormatTimeWithReason(3, "You haven't verified your minecraft account"),
                    "https://sky.coflnet.com/player/" + socket.SessionInfo.McUuid,
                    FormatLines(
                        "To verify your minecraft account, bid",
                        "the exact amount the message says on a random auction.",
                        "Alternatively click this link and then `Claim account`.")))
            .If(() => delayAmount >= TimeSpan.FromSeconds(8), db => db
                .MsgLine(FormatTimeWithReason(8, "One of your accounts got blacklisted for bad behaviour"), null,
                    FormatLines(
                        "Your account was manually reviewed",
                        "and found to be actively violating the rules.",
                        "Most likely you used unfair advantages over extended periods of time.",
                        "You can't remove this delay after it has been activated")))
            .If(() => delayAmount == TimeSpan.FromSeconds(12), db => db
                .CoflCommand<CaptchaCommand>(FormatTimeWithReason(12, $"You haven't solved the anti afk captcha, {McColorCodes.ITALIC}click to get one"),
                    "",
                    FormatLines(
                        "You flipped for 4 or more hours in the last day.",
                        $"Do {McColorCodes.AQUA}/cofl captcha{McColorCodes.YELLOW} to get a new captcha (or click this).",
                        "The delay will be removed after you solve the captcha 10 seconds before the next api update")));


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