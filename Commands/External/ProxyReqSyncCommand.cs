using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Request client sync
/// </summary>
public class ProxyReqSyncCommand : McCommand
{
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        SendState(socket);
        socket.sessionLifesycle.AccountInfo.OnChange += (a) => SendState(socket);
        socket.sessionLifesycle.FlipSettings.OnChange += (a) => SendState(socket);
        socket.sessionLifesycle.OnDelayChange += (a) => SendState(socket);
        socket.OnConClose += () =>
        {
            socket.sessionLifesycle.AccountInfo.OnChange -= (a) => SendState(socket);
            socket.sessionLifesycle.FlipSettings.OnChange -= (a) => SendState(socket);
            socket.sessionLifesycle.OnDelayChange -= (a) => SendState(socket);
        };
        return Task.CompletedTask;
    }

    private static void SendState(MinecraftSocket socket)
    {
        socket.Send(Response.Create("proxySync", new Format()
        {
            Settings = socket.Settings,
            SessionInfo = socket.SessionInfo,
            AccountInfo = socket.AccountInfo,
            ApproxDelay = socket.sessionLifesycle.CurrentDelay.TotalMilliseconds
        }));
    }

    public class Format
    {
        public FlipSettings Settings { get; set; }
        public SessionInfo SessionInfo { get; set; }
        public AccountInfo AccountInfo { get; set; }
        public double ApproxDelay { get; set; }
    }
}