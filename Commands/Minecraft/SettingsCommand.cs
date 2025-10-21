using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Information about mod settings", "Usage: /cofl settings")]
public class SettingsCommand : McCommand
{
    public override bool IsPublic => true;

    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(db => db
            .MsgLine($"{McColorCodes.YELLOW}Manage your mod and flipping settings here:")
            .MsgLine($"List and change all settings with {McColorCodes.AQUA}/cofl set{McColorCodes.RESET}", "/cofl set", "Open settings")
            .MsgLine($"Browse premade settings with {McColorCodes.AQUA}/cofl config{McColorCodes.RESET}", "/cofl config", "List premade configs")
            .MsgLine($"Manage items via {McColorCodes.AQUA}/cofl whitelist{McColorCodes.RESET} and {McColorCodes.AQUA}/cofl blacklist{McColorCodes.RESET}", "/cofl whitelist", "Open whitelist")
            .MsgLine($"{McColorCodes.YELLOW}Or use the flipper page to updatesettings.", "https://sky.coflnet.com/flipper", $"[Open flip page]\nit does not contain all settings yet\n{McColorCodes.GRAY}but you may find it easier to use.")
        );

        return Task.CompletedTask;
    }
}
