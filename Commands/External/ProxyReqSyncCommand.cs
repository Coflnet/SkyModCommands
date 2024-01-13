using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Test;
using Coflnet.Sky.Items.Client.Model;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Request client sync
/// </summary>
public class ProxyReqSyncCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        SendState(socket);
        socket.sessionLifesycle.AccountInfo.OnChange += (a) => SendState(socket);
        socket.sessionLifesycle.FlipSettings.ShouldPreventUpdate += (a) =>
        {
            SendState(socket);
            return false;
        };
        socket.sessionLifesycle.OnDelayChange += (a) => SendState(socket);
        socket.OnConClose += () =>
        {
            socket.sessionLifesycle.AccountInfo.OnChange -= (a) => SendState(socket);
            socket.sessionLifesycle.FlipSettings.OnChange -= (a) => SendState(socket);
            socket.sessionLifesycle.OnDelayChange -= (a) => SendState(socket);
        };
        var filterState = socket.GetService<FilterStateService>().State;
        if (await socket.UserAccountTier() >= AccountTier.PREMIUM_PLUS)
            socket.Send(Response.Create("filterData", filterState));
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