using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Sets a custom ah gui overlay", "Usage: /cofl setgui <gui>")]
public class SetGuiCommand : McCommand
{
    public override bool IsPublic => true;

    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(db => db.MsgLine("This command is clientside only. Seems like you are not using the official mod."));
        return Task.CompletedTask;
    }
}