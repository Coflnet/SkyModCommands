using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public static class ModLoginExtensions
{
    public static async Task SendLoginPrompt(this IMinecraftSocket socket)
    {
        var sessionId = socket.SessionInfo.SessionId;
        using var UserId = await SelfUpdatingValue<string>.Create(sessionId, "userId", () =>
        {
            socket.SendMessage("You are not logged in. Please log in first.");
            (socket as MinecraftSocket)?.ModAdapter.SendLoginPrompt(socket.sessionLifesycle.GetAuthLink(sessionId));
            return null;
        });
        if (UserId.Value == null)
        {
            return;
        }
        await socket.sessionLifesycle.LoggedIn(UserId.Value);
        socket.SendMessage("Login completed, please rerun the last command");
    }
}