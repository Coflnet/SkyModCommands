using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Tutorials;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Shows your current delay",
        "To allow everyone to get some flips, each",
        "user gets delayed when he is found to buy too fast",
        "The delay decreases over time",
        "and is not fully applied to all flips",
        "You can reduce this by buying slower",
        "Very high profit flips are excepted from this")]
    public class DelayCommand : McCommand
    {
        public override bool IsPublic => true;
        public ConcurrentDictionary<string, int> CallCount = new();
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var reduction = CallCount.GetValueOrDefault(socket.UserId) * TimeSpan.FromMicroseconds(1);
            if (socket.sessionLifesycle.CurrentDelay >= TimeSpan.FromSeconds(2))
                reduction = TimeSpan.Zero;
            var delayAmount = socket.sessionLifesycle.CurrentDelay - reduction;
            var macroDelay = socket.sessionLifesycle.MacroDelay;
            Activity.Current?.AddTag("delay", delayAmount.ToString()).AddTag("macroDelay", macroDelay.ToString());
            if (await socket.UserAccountTier() == 0 && socket.SessionInfo.SessionTier == AccountTier.NONE)
            {
                Activity.Current?.AddTag("freeVersion", true);
                socket.Dialog(db => db.MsgLine($"You are using the {McColorCodes.YELLOW}free version{DEFAULT_COLOR} and are thus delayed by over a minute.", "https://sky.coflnet.com/premium", "Opens the premium page")
                            .MsgLine($"Purchase premium to remove delay. {McColorCodes.AQUA}/cofl buy", "/cofl buy", "Shows the premium options"));
                return;
            }
            if (!socket.SessionInfo.FlipsEnabled)
            {
                socket.Dialog(db => db.CoflCommand<FlipCommand>("You don't have flips enabled.\nClick to toggle flips", "", "Click to toggle them"));
                return;
            }

            if (socket.SessionInfo.clientSessionId.Length == 7 && socket.SessionInfo.clientSessionId.EndsWith("idd"))
            {
                socket.Dialog(db => db.MsgLine("You are using a random middleman account, this account is delayed by 1 minute please contact me (Äkwav)", null, "This account is used for testing purposes"));
                return;
            }
            var called = CallCount.AddOrUpdate(socket.UserId, 1, (k, v) => v + 1);
            if (Random.Shared.NextDouble() < 0.01 * (called - 100))
            {
                delayAmount += TimeSpan.FromSeconds(8);
            }
            Activity.Current?.AddTag("count", called);
            if (delayAmount <= TimeSpan.Zero)
                socket.SendMessage(COFLNET + $"You are currently not delayed at all :)", null, "Enjoy flipping at full speed☻");
            else if (delayAmount == TimeSpan.FromSeconds(12))
                socket.Dialog(db => db.CoflCommand<CaptchaCommand>($"You flipped for too long and have to solve a captcha to remove your 12 second delay {McColorCodes.AQUA}click to get one", "", "Generates a new captcha"));
            else
                socket.SendMessage(COFLNET + $"You are currently delayed by {McColorCodes.AQUA}{delayAmount.TotalSeconds}s{McColorCodes.GRAY} on api flips by the fairness system. This will decrease over time and is not fully applied to all flips.",
                        null, McColorCodes.GRAY + "Your call to this has been recorded, \nattempts to trick the system will be punished.");
            if (socket.SessionInfo.SessionTier == AccountTier.SUPER_PREMIUM && delayAmount > TimeSpan.Zero)
                socket.SendMessage(COFLNET + $"While using {McColorCodes.RED}pre api{DEFAULT_COLOR} your delay increases {McColorCodes.GREEN}{DelayHandler.DelayReduction * 100}% slower{DEFAULT_COLOR} "
                    + $"and is capped at {McColorCodes.GREEN}{DelayHandler.MaxSuperPremiumDelay.TotalSeconds} seconds.",
                    null, "Enjoy flipping at high speed☻");
            else if (socket.SessionInfo.SessionTier == AccountTier.PREMIUM)
                socket.SendMessage($"Note that because you are not on the prem+ tier flips with {McColorCodes.DARK_GRAY}! {McColorCodes.RESET}or {McColorCodes.DARK_GRAY}.{McColorCodes.RESET} behind the item name are sent to you 1000 milliseconds later. If they are more than 1m profit you may not get them anymore because a prem+ user already purchased them.");
            if ((socket.Settings?.Visibility?.LowestBin ?? false) && socket.Settings.AllowedFinders != Core.LowPricedAuction.FinderType.SNIPER)
                ShowWarning(socket, "show lowest bin", "showlbin");
            if ((socket.Settings?.BasedOnLBin ?? false) && socket.Settings.AllowedFinders != Core.LowPricedAuction.FinderType.SNIPER)
                ShowWarning(socket, "profit based on lowest bin", "lbin");
            if (socket.Settings?.Visibility?.SecondLowestBin ?? false)
                ShowWarning(socket, "show second lowest bin", "showslbin");
            if (socket.Settings?.Visibility?.Seller ?? false)
                ShowWarning(socket, "show seller name", "showseller");

            if (socket.SessionInfo.LicensePoints > 0 && delayAmount > TimeSpan.Zero)
            {
                await Task.Delay(10);
                var adjustedDelay = delayAmount;
                adjustedDelay /= Math.Pow(0.945, socket.SessionInfo.LicensePoints);
                adjustedDelay += TimeSpan.FromSeconds(0.01 * socket.SessionInfo.LicensePoints);
                var delayReducedBy = delayAmount - adjustedDelay;
                socket.Dialog(db => db.MsgLine($"Because of your license(s) your delay is reduced by {McColorCodes.AQUA}{delayReducedBy.TotalSeconds}s{DEFAULT_COLOR} from {McColorCodes.RED}{adjustedDelay.TotalSeconds}s{DEFAULT_COLOR}."));
            }
            if(socket.SessionInfo.NoSharedDelay)
            {
                socket.Dialog(db => db.MsgLine("Your license granted you no shared delay for this connection"));
            }

            await socket.TriggerTutorial<DelayTutorial>();
            if (delayAmount >= TimeSpan.FromSeconds(1))
            {
                socket.SendMessage(GetSupportText(socket, delayAmount));
            }
        }

        private static void ShowWarning(MinecraftSocket socket, string settingName, string key)
        {
            socket.Dialog(db => db.CoflCommand<SetCommand>($"You have the setting {McColorCodes.ITALIC}{settingName}{McColorCodes.RESET} enabled, this can drastically slow down flips.\n{McColorCodes.GREEN}Click to disable it", $"{key} false", $"Disables {McColorCodes.AQUA}{settingName}"));
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
            .If(() => delayAmount >= TimeSpan.FromSeconds(8) && delayAmount < TimeSpan.FromSeconds(12) || delayAmount >= TimeSpan.FromSeconds(16), db => db
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