using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

public class CopySettingsCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var isModerator = socket.GetService<ModeratorService>().IsModerator(socket);
        if (!isModerator)
            throw new CoflnetException("forbidden", "Whoops, you don't seem to be a moderator. Therefore you can't copy settings");

        var id = arguments.Trim('"');
        socket.sessionLifesycle.FlipSettings = await SelfUpdatingValue<FlipSettings>.Create(id, "flipSettings");
        socket.Dialog(db => db.MsgLine("Loaded settings").AsGray());
    }
}
