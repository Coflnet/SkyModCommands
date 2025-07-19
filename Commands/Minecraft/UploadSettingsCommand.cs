using System.Threading.Tasks;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC;

public class UploadSettingsCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var settings = Convert<Dictionary<string, string>>(arguments);
        socket.Dialog(db => db.MsgLine($"Received {settings.Count}"));
    }
}
