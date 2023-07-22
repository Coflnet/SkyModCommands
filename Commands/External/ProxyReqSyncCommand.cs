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
        socket.Send(Response.Create("proxySync", socket.Settings));
        return Task.CompletedTask;
    }

    public class Format
    {
        public FlipSettings Settings { get; set; }
    }
}