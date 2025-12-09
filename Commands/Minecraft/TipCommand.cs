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
    "Tip players in supported gamemodes",
    "Usage: /tip <player> <gamemode>",
    "Supported gamemodes: arcade, skywars, tntgames, legacy",
    "Each player can only tip one other player per gamemode",
    "Tips are automatically sent every minute to online players",
    "Use /cofl set blockAutotip true to disable automatic tipping")]
public class TipCommand : McCommand
{
    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                // Show help and usage
                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.YELLOW}Autotip Command Help:")
                    .MsgLine($"{McColorCodes.AQUA}Usage: /tip <player> <gamemode>")
                    .MsgLine($"{McColorCodes.GRAY}Supported gamemodes: {string.Join(", ", AutotipService.SupportedGamemodes)}")
                    .MsgLine($"{McColorCodes.GRAY}Each player can only tip one other player per gamemode")
                    .MsgLine($"{McColorCodes.GRAY}Tips are automatically sent every minute")
                    .MsgLine($"{McColorCodes.YELLOW}Examples:")
                    .MsgLine($"{McColorCodes.WHITE}/tip Notch arcade")
                    .MsgLine($"{McColorCodes.WHITE}/tip Hypixel skywars")
                    .MsgLine($"{McColorCodes.GRAY}To disable autotip: {McColorCodes.AQUA}/cofl set blockAutotip true")
                );
                return;
            }

            var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (args.Length < 2)
            {
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}Please provide both player name and gamemode. Usage: /tip <player> <gamemode>"));
                return;
            }

            var targetPlayer = args[0];
            var gamemode = args[1].ToLowerInvariant();

            // Validate gamemode
            if (!AutotipService.SupportedGamemodes.Contains(gamemode))
            {
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}Unsupported gamemode '{gamemode}'. Supported: {string.Join(", ", AutotipService.SupportedGamemodes)}"));
                return;
            }

            // Validate player name
            if (string.IsNullOrWhiteSpace(targetPlayer) || targetPlayer.Length < 3 || targetPlayer.Length > 16)
            {
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}Invalid player name. Player names must be 3-16 characters long."));
                return;
            }

            // Check if user is logged in
            if (string.IsNullOrEmpty(socket.UserId))
            {
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}You must be logged in to use the tip command. Please use /cofl login"));
                return;
            }

            // Get autotip service
            var autotipService = socket.GetService<AutotipService>();
            if (autotipService == null)
            {
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}Autotip service is not available. Please try again later."));
                return;
            }

            // Execute the manual tip
            var success = await autotipService.ExecuteManualTip(socket, targetPlayer, gamemode);
            
            if (success)
            {
                socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.GREEN}Successfully sent tip to {McColorCodes.AQUA}{targetPlayer}{McColorCodes.GREEN} in {McColorCodes.YELLOW}{gamemode}{McColorCodes.GREEN}!"));
                
                // Show info about automatic tipping
                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.GRAY}Automatic tips are sent every minute to online players")
                    .MsgLine($"{McColorCodes.GRAY}To disable autotip: {McColorCodes.AQUA}/cofl set blockAutotip true")
                );
            }
            else
            {
                // Check if autotip is blocked
                var accountSettings = socket.sessionLifesycle?.AccountSettings?.Value;
                bool isBlocked = false;
                if (accountSettings != null)
                {
                    // Use reflection to check for BlockAutotip property (in case it doesn't exist in the shared library yet)
                    var blockAutotipProp = accountSettings.GetType().GetProperty("BlockAutotip");
                    isBlocked = blockAutotipProp != null && (bool)(blockAutotipProp.GetValue(accountSettings) ?? false);
                }

                if (isBlocked)
                {
                    socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.YELLOW}Autotip is currently disabled. Enable it with: {McColorCodes.AQUA}/cofl set blockAutotip false"));
                }
                else
                {
                    socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}Failed to send tip. You may have already tipped someone in {gamemode} recently."));
                }
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