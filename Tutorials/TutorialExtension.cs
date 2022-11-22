using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.ModCommands.Tutorials;

namespace Coflnet.Sky.Commands.MC;

public static class TutorialExtension
{
    public static async Task TriggerTutorial<Tutorial>(this IMinecraftSocket socket) where Tutorial : TutorialBase
    {
        await socket.GetService<ITutorialService>().Trigger<Tutorial>(socket);
    }
}
