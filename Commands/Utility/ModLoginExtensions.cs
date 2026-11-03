using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public static class ModLoginExtensions
{
    public static async Task SendLoginPrompt(this IMinecraftSocket socket)
    {
        var sessionId = socket.SessionInfo.SessionId;
        var UserId = await SelfUpdatingValue<string>.Create(sessionId, "userId", () =>
        {
            socket.SendMessage("You are not logged in. Please log in first.");
            (socket as MinecraftSocket)?.ModAdapter.SendLoginPrompt(socket.sessionLifesycle.GetAuthLink(sessionId));
            return null;
        });
        // the socket can close while the value is created, nobody would release it then
        var current = socket.sessionLifesycle;
        if (UserId.Value == null || current == null || current.IsDisposed)
        {
            UserId.Dispose();
            return;
        }
        // the session owns the value from here on and disposes it when it ends
        ModSessionLifesycle.ReplaceValue(ref current.UserId, UserId);
        if (current.IsDisposed)
        {
            UserId.Dispose();
            return;
        }
        await current.LoggedIn(UserId.Value);
        socket.SendMessage("Login completed, please rerun the last command");
    }
}