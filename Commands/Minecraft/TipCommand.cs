using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Manage the autotip feature",
    "Enables or disables automatic tipping of players",
    "You can also view your autotip statistics")]
public class AutoTipCommand : McCommand
{
    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        try
        {
            var subCommand = Convert<string>(arguments).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(subCommand))
            {
                // Show help and usage
                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.YELLOW}Autotip Command Help:")
                    .MsgLine($"{McColorCodes.GRAY}/cofl autotip enable {McColorCodes.WHITE}- Enable automatic tipping")
                    .MsgLine($"{McColorCodes.GRAY}/cofl autotip disable {McColorCodes.WHITE}- Disable automatic tipping")
                    .MsgLine($"{McColorCodes.GRAY}/cofl autotip status {McColorCodes.WHITE}- View current autotip status")
                    .MsgLine($"{McColorCodes.GRAY}/cofl autotip stats {McColorCodes.WHITE}- View your autotip statistics")
                );
                return;
            }

            // Get autotip service
            var autotipService = socket.GetService<AutotipService>();
            if (autotipService == null)
            {
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}Autotip service is not available. Please try again later."));
                return;
            }

            if (subCommand == "enable")
            {
                socket.sessionLifesycle.AccountSettings.Value.BlockAutotip = false;
                await socket.sessionLifesycle.AccountSettings.Update();
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.GREEN}Autotip has been enabled."));
            }
            else if (subCommand == "disable")
            {
                socket.sessionLifesycle.AccountSettings.Value.BlockAutotip = true;
                await socket.sessionLifesycle.AccountSettings.Update();
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.YELLOW}Autotip has been disabled."));
            }
            else if (subCommand == "status")
            {
                var isBlocked = socket.sessionLifesycle.AccountSettings.Value.BlockAutotip;
                var statusText = isBlocked ? $"{McColorCodes.RED}Disabled" : $"{McColorCodes.GREEN}Enabled";
                var actionText = isBlocked ? "enable" : "disable";
                
                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.YELLOW}Autotip Status: {statusText}")
                    .MsgLine($"{McColorCodes.GRAY}Automatic tipping is currently {(isBlocked ? "disabled" : "enabled")}")
                    .MsgLine($"{McColorCodes.GRAY}Use {McColorCodes.AQUA}/cofl autotip {actionText}{McColorCodes.GRAY} to {actionText} it"));
            }
            else if (subCommand == "stats")
            {
                System.Collections.Generic.List<ModCommands.Models.AutotipEntry> stats = await autotipService.GetUserTipHistory(socket.UserId, 1000);
                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.YELLOW}Autotip Statistics:")
                    .MsgLine($"{McColorCodes.GRAY}Tips Sent in the last week: {(stats.Count >=1000?"more than ":"")}{McColorCodes.WHITE}{stats.Count}")
                );
            }
            else
            {
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}Unknown subcommand. Please use 'enable', 'disable', 'status' or 'stats'."));
            }

        }
        catch (ArgumentException ex)
        {
            socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}{ex.Message}"));
        }
        catch (InvalidOperationException ex)
        {
            socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.YELLOW}{ex.Message}"));
        }
        catch (Exception ex)
        {
            dev.Logger.Instance.Error(ex, "Error executing tip command");
            socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}An error occurred while processing your tip. Please try again later."));
        }
    }
}