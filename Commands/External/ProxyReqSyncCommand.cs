using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    DateTime lastSync = DateTime.MinValue;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(db => db.MsgLine($"Syncing settings..."));
        Activity.Current.Log("Context " + arguments);
        for (int i = 0; i < 50; i++)
        {
            if (socket.Settings.IsCompiled)
                break;
            await Task.Delay(200);
        }
        if (!socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.PREMIUM_PLUS))
        {
            for (int i = 0; i < 5; i++)
            {
                await socket.sessionLifesycle.TierManager.RefreshTier();
                await Task.Delay(1500);
                if (socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.PREMIUM_PLUS))
                    break;
            }
            if (!socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.PREMIUM_PLUS))
            {
                Activity.Current.Log("Main instance could not verify your premium status. Please try again later.");
                socket.Dialog(db => db.MsgLine("Main instance could not verify your premium status. Please try again later."));
            }
        }
        SendGlobalState(socket);
        SendState(socket);
        Action<object> updateHandler = (a) => SendState(socket);
        socket.sessionLifesycle.AccountInfo.OnChange += updateHandler;
        socket.sessionLifesycle.FlipSettings.ShouldPreventUpdate += (a) =>
        {
            SendState(socket);
            return false;
        };
        socket.sessionLifesycle.OnDelayChange += (a) => SendState(socket);
        socket.OnConClose += () =>
        {
            socket.sessionLifesycle.AccountInfo.OnChange -= updateHandler;
            socket.sessionLifesycle.FlipSettings.OnChange -= updateHandler;
            socket.sessionLifesycle.OnDelayChange -= (a) => SendState(socket);
        };
    }

    private void SendGlobalState(MinecraftSocket socket)
    {
        if (lastSync < DateTime.UtcNow.AddMinutes(-1))
        {
            lastSync = DateTime.UtcNow;
            var filterState = socket.GetService<FilterStateService>().State;
            socket.Send(Response.Create("filterData", filterState));
            socket.Send(Response.Create("exemptKeys", socket.GetService<IDelayExemptList>().Exemptions));
        }
    }

    private static void SendState(MinecraftSocket socket)
    {
        using var sync = socket.CreateActivity("settingsSync");
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