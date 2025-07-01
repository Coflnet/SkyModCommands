using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

public class NoLoginCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        await socket.GetService<StayLoggedOutService>().SetLoggedOut(socket.SessionInfo.SessionId, System.DateTime.UtcNow.AddDays(30));
        socket.Dialog(db => db.MsgLine("Alright, this message won't show again for a while. Some features may not work."));
    }
}
