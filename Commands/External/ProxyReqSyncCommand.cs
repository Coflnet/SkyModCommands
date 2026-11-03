using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Test;
using Coflnet.Sky.Items.Client.Model;
using Newtonsoft.Json;

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
            if (socket.IsClosed)
                return;
            if (socket.Settings.IsCompiled)
                break;
            await Task.Delay(300);
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
        await Task.Delay(200);
        SendState(socket);
        var lifesycle = socket.sessionLifesycle;
        if (socket.IsClosed || lifesycle == null || !socket.TryRegisterProxySync())
            return;
        var sync = WireStateSync(socket, lifesycle, () => SendState(socket));
        socket.OnConClose += sync.Stop;
    }

    internal static StateSyncHandlers WireStateSync(MinecraftSocket socket, ModSessionLifesycle lifesycle, Action send)
    {
        var sync = new StateSyncHandlers(lifesycle, send);
        sync.Attach();
        lifesycle.ValuesReplaced += sync.Attach;
        lifesycle.OnDelayChange += sync.OnDelay;
        return sync;
    }

    /// <summary>
    /// Sends the state on changes of the account info and flip settings. The session replaces those values
    /// when the settings are subscribed again, so the handlers are moved to the current instances on <see cref="Attach"/>.
    /// </summary>
    internal class StateSyncHandlers
    {
        private readonly Action send;
        private readonly ModSessionLifesycle lifesycle;
        private SelfUpdatingValue<AccountInfo> accountInfo;
        private SelfUpdatingValue<FlipSettings> flipSettings;
        private readonly object attachLock = new();
        private bool stopped;

        public StateSyncHandlers(ModSessionLifesycle lifesycle, Action send)
        {
            this.lifesycle = lifesycle;
            this.send = send;
        }

        public void OnDelay(TimeSpan _) => send();

        private void OnAccount(AccountInfo _) => send();

        private void OnSettings(FlipSettings _) => send();

        public void Stop()
        {
            lifesycle.ValuesReplaced -= Attach;
            lifesycle.OnDelayChange -= OnDelay;
            lock (attachLock)
            {
                stopped = true;
                Detach();
            }
        }

        public void Attach()
        {
            lock (attachLock)
            {
                if (stopped)
                    return;
                Detach();
                accountInfo = lifesycle.AccountInfo;
                flipSettings = lifesycle.FlipSettings;
                // AfterChange runs once the value is assigned, ShouldPreventUpdate is owned by the lifecycle
                if (accountInfo != null)
                    accountInfo.AfterChange += OnAccount;
                if (flipSettings != null)
                    flipSettings.AfterChange += OnSettings;
            }
        }

        public void Detach()
        {
            lock (attachLock)
            {
                if (accountInfo != null)
                    accountInfo.AfterChange -= OnAccount;
                if (flipSettings != null)
                    flipSettings.AfterChange -= OnSettings;
                accountInfo = null;
                flipSettings = null;
            }
        }
    }

    private void SendGlobalState(MinecraftSocket socket)
    {
        if (lastSync < DateTime.UtcNow.AddSeconds(-1))
        {
            lastSync = DateTime.UtcNow;
            var filterState = socket.GetService<FilterStateService>().State;
            socket.Send(Response.Create("filterData", filterState));
            socket.Send(Response.Create("exemptKeys", socket.GetService<IDelayExemptList>().Exemptions));
        }
    }

    private static void SendState(MinecraftSocket socket)
    {
        var lifesycle = socket.sessionLifesycle;
        if (socket.IsClosed || lifesycle == null)
            return;
        var settings = lifesycle.FlipSettings?.Value;
        var accountInfo = lifesycle.AccountInfo?.Value;
        if (settings == null || accountInfo == null)
            return;
        if (!ReferenceEquals(settings.PlayerInfo, socket.SessionInfo))
            settings.PlayerInfo = socket.SessionInfo;
        using var sync = socket.CreateActivity("settingsSync");
        socket.Send(Create("proxySync", new Format()
        {
            Settings = settings,
            SessionInfo = socket.SessionInfo,
            AccountInfo = accountInfo,
            ApproxDelay = lifesycle.CurrentDelay.TotalMilliseconds
        }));
    }


    private static JsonSerializerSettings Settings = new JsonSerializerSettings()
    {
        Converters = [new Newtonsoft.Json.Converters.StringEnumConverter()]
    };

    public static Response Create<T>(string type, T data)
    {
        return new Response(type, JsonConvert.SerializeObject(data, Formatting.None, Settings));
    }

    public class Format
    {
        public FlipSettings Settings { get; set; }
        public SessionInfo SessionInfo { get; set; }
        public AccountInfo AccountInfo { get; set; }
        public double ApproxDelay { get; set; }
    }
}
