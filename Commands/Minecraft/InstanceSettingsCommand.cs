using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Keep settings for this mod instance without an account",
    "Usage: /cl instancesettings <enable|disable|copy>",
    "enable: keeps your settings for a year (even while not logged in)",
    "disable: forgets the stored settings",
    "copy: copies the stored settings to your account after login")]
public class InstanceSettingsCommand : McCommand
{
    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var action = arguments.Trim('"').Trim().ToLower();
        switch (action)
        {
            case "enable":
                await Enable(socket);
                break;
            case "disable":
                await socket.sessionLifesycle.DisableInstanceSettings();
                socket.Dialog(db => db.MsgLine("Settings are no longer kept for this mod instance"));
                break;
            case "copy":
                await CopyToAccount(socket);
                break;
            default:
                PrintStatus(socket);
                break;
        }
    }

    private static async Task Enable(MinecraftSocket socket)
    {
        if (!string.IsNullOrEmpty(socket.sessionLifesycle.UserId))
        {
            socket.Dialog(db => db.MsgLine("You are logged in, your settings are already saved with your account"));
            return;
        }
        await socket.sessionLifesycle.EnableInstanceSettings();
        socket.Dialog(db => db.MsgLine($"Your settings are now kept for this mod instance for a year. "
            + $"Use {McColorCodes.AQUA}/cofl instancesettings disable{McColorCodes.RESET} to stop that"));
    }

    private static async Task CopyToAccount(MinecraftSocket socket)
    {
        _ = socket.UserId; // throws a login hint when not logged in
        var stored = await socket.GetService<IInstanceSettingsService>().Get(socket.SessionInfo.SessionId);
        if (stored == null)
            throw new CoflnetException("no_instance_settings", "There are no settings stored for this mod instance");
        stored.Changer = null;
        stored.LastChanged = "Settings of this mod instance";
        await socket.sessionLifesycle.SaveFlipSettings(stored);
        // the account holds them now, do not offer the copy on every reconnect
        await socket.sessionLifesycle.DisableInstanceSettings();
        socket.Dialog(db => db.MsgLine("Copied the settings of this mod instance to your account"));
    }

    private static void PrintStatus(MinecraftSocket socket)
    {
        var enabled = socket.sessionLifesycle.SavesSettingsForInstance;
        socket.Dialog(db => db.MsgLine($"Settings are {(enabled ? McColorCodes.GREEN + "kept" : McColorCodes.RED + "not kept")}{McColorCodes.RESET} for this mod instance")
            .CoflCommand<InstanceSettingsCommand>($"{McColorCodes.AQUA}[Enable]", "enable", "Keep settings for this mod instance for a year without an account")
            .CoflCommand<InstanceSettingsCommand>($" {McColorCodes.AQUA}[Disable]", "disable", "Forget the settings stored for this mod instance")
            .CoflCommand<InstanceSettingsCommand>($" {McColorCodes.AQUA}[Copy to account]", "copy", "Copy the stored settings to your account (requires login)"));
    }
}
