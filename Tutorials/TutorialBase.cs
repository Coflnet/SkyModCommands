using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

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
