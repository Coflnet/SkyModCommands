using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

public class DialogCommand : McCommand
{
    private static DialogService DialogService = new DialogService();
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        var response = DialogService.GetResponse(socket, arguments.Trim('"'));
        if (response != null)
            socket.SendMessage(response);

        return Task.CompletedTask;
    }
}
