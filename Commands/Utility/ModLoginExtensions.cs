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
        await AdoptLogin(socket, UserId);
    }

    /// <summary>
    /// Hands the loaded user id to the session and completes the login.
    /// Disposes the value when there is no user or the session ended while it was created, nobody would release it then
    /// </summary>
    internal static async Task AdoptLogin(IMinecraftSocket socket, SelfUpdatingValue<string> userId)
    {
        var current = socket.sessionLifesycle;
        if (userId.Value == null || current == null)
        {
            userId.Dispose();
            return;
        }
        // the session owns the value from here on and disposes it when it ends
        if (!current.TryAdopt(ref current.UserId, userId))
            return;
        await current.LoggedIn(userId.Value);
        socket.SendMessage("Login completed, please rerun the last command");
    }
}