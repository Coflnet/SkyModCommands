using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.ModCommands.Tutorials;
public abstract class TutorialBase
{
    public readonly string Name;

    public TutorialBase()
    {
        Name = GetType().Name;
    }

    public abstract void Trigger(DialogBuilder builder, IMinecraftSocket socket);

}

public class Welcome : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"Hello {McColorCodes.AQUA}{socket.SessionInfo.McName}")
            .MsgLine($"It looks like this is the first time using the §1C§6oflmod§f");
    }
}

public static class TutorialExtension
{
    public static async Task TriggerTutorial<Tutorial>(this IMinecraftSocket socket) where Tutorial : TutorialBase
    {
        await socket.GetService<ITutorialService>().Trigger<Tutorial>(socket);
    }
}
