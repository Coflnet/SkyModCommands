namespace Coflnet.Sky.Commands.MC;

public static class ModLoginExtensions
{
    public static void SendLoginPrompt(this IMinecraftSocket socket)
    {
        socket.SendMessage("You are not logged in. Please log in first.");
        (socket as MinecraftSocket)?.ModAdapter.SendLoginPrompt(socket.sessionLifesycle.GetAuthLink(socket.SessionInfo.SessionId));
    }
}