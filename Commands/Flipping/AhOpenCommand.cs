using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class AhOpenCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var name = await GetMcNameForCommand.GetName(socket, arguments);
        using var span = socket.CreateActivity("ahopen", socket.ConSpan);
        span.SetTag("name", name);
        socket.ExecuteCommand($"/ah {name}");
    }
}
