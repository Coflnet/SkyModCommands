using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

public class TutorialCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        await socket.GetService<ITutorialService>().CommandInput(socket, arguments.Trim('"'));
    }

}
