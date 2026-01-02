using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Manage your Rust Finder add-on",
        "Usage: /cofl rust",
        "Shows your Rust Finder add-on status and allows you to purchase or use it",
        "The Rust Finder detects items with hidden stats that are underpriced")]
    public class RustAddonCommand : McCommand
    {
        public override bool IsPublic => true;

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var currentTier = await socket.sessionLifesycle.TierManager.GetCurrentCached();
            var isOwned = socket.SessionInfo.RustAddonOwned;
            if (isOwned == null)
            {
                try
                {
                    var userApi = socket.GetService<Payments.Client.Api.UserApi>();
                    var owns = await userApi.UserUserIdOwnsUntilPostAsync(socket.UserId, new() { "rust-addon" });
                    socket.SessionInfo.RustAddonOwned = owns.ContainsKey("rust-addon") && owns["rust-addon"] > DateTime.UtcNow;
                }
                catch (Exception e)
                {
                    socket.Error(e, "checking rust addon ownership");
                }
            }

            // Build the dialog
            var dialogBuilder = DialogBuilder.New;

            // Title and status
            dialogBuilder.MsgLine($"{McColorCodes.LIGHT_PURPLE}Rust Finder Add-on Status");

            if (isOwned == true)
            {
                dialogBuilder.MsgLine($"{McColorCodes.GREEN}✓ You own the Rust Finder add-on");
                dialogBuilder.Msg($"{McColorCodes.GRAY}The Rust Finder is enabled in your finders list.");

                if (socket.Settings?.AllowedFinders.HasFlag(LowPricedAuction.FinderType.Rust) ?? false)
                {
                    dialogBuilder.MsgLine($"{McColorCodes.GREEN} It is currently {McColorCodes.BOLD}ENABLED{McColorCodes.RESET} and will find flips");
                    dialogBuilder.CoflCommand<SetCommand>(
                        $"{McColorCodes.YELLOW}Disable Rust Finder",
                        "finders " + (socket.Settings.AllowedFinders & ~LowPricedAuction.FinderType.Rust),
                        "Click to disable the Rust Finder");
                }
                else
                {
                    dialogBuilder.MsgLine($"{McColorCodes.YELLOW}It is currently {McColorCodes.BOLD}DISABLED{McColorCodes.RESET}");
                    dialogBuilder.CoflCommand<SetCommand>(
                        $"{McColorCodes.GREEN}Enable Rust Finder",
                        "finders " + (socket.Settings.AllowedFinders | LowPricedAuction.FinderType.Rust),
                        "Click to enable the Rust Finder");
                }
            }
            else if (isOwned == false)
            {
                dialogBuilder.MsgLine($"{McColorCodes.RED}✗ You do not own the Rust Finder add-on");
                dialogBuilder.Msg($"{McColorCodes.GRAY}Purchase it for {McColorCodes.AQUA}1200 CoflCoins{McColorCodes.GRAY} for 30 days of access.");

                // Check if user can purchase
                if (currentTier >= AccountTier.PREMIUM)
                {
                    dialogBuilder.LineBreak();
                    dialogBuilder.CoflCommand<PurchaseCommand>(
                        $"{McColorCodes.LIGHT_PURPLE}Purchase Rust Finder Add-on",
                        "rust-addon 1",
                        "Click to purchase the Rust Finder add-on");
                }
                else
                {
                    dialogBuilder.LineBreak();
                    dialogBuilder.MsgLine($"{McColorCodes.RED}You need at least {McColorCodes.GOLD}Premium{McColorCodes.WHITE} to purchase this add-on");
                    dialogBuilder.CoflCommand<PurchaseCommand>(
                        $"{McColorCodes.GOLD}Upgrade to Premium",
                        "premium 1",
                        "Click to purchase Premium");
                }
            }
            else
            {
                // Ownership status is being checked
                dialogBuilder.MsgLine($"{McColorCodes.YELLOW}Checking ownership status...");
                dialogBuilder.Msg($"{McColorCodes.GRAY}Please wait, we're verifying your Rust Finder add-on status.");
            }

            dialogBuilder.LineBreak();
            dialogBuilder.MsgLine($"{McColorCodes.DARK_GRAY}The Rust Finder is an alternatively developed finder.");

            socket.SendMessage(dialogBuilder);
        }
    }
}
