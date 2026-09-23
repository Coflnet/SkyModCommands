using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

#nullable enable
public interface IAccountTierManager : IDisposable
{
    bool HasAtLeast(AccountTier tier);
    Task<(AccountTier tier, DateTime expiresAt)> GetCurrentTierWithExpire(bool forceUpdate = false);
    Task<AccountTier> GetCurrentCached();
    DateTime ExpiresAt { get; }
    string? DefaultAccount { get; }
    bool IsLicense { get; }

    string GetSessionInfo();
    void InvalidateCache();
    Task RefreshTier();
    bool IsConnectedFromOtherAccount(out string otherAccount, out AccountTier tier);
    event EventHandler<AccountTier>? OnTierChange;
    Task ChangeDefaultTo(string mcUuid);
    bool IsNewConnection();
}

public class AccountTierManager : IAccountTierManager
{
    private readonly IMinecraftSocket socket;
    private SelfUpdatingValue<ActiveSessions>? activeSessions;
    public event EventHandler<AccountTier>? OnTierChange;
    private AccountTier? lastTier;
    private DateTime expiresAt;
    private DateTime nextTierRefresh;
    private string userId = string.Empty;
    IAuthUpdate loginNotification;
    public DateTime ExpiresAt => expiresAt;
    bool isNewConnection = false;

    public string? DefaultAccount => activeSessions?.Value?.UseAccountTierOn;
    private bool Disposed { get; set; }

    public bool IsLicense { get; private set; }

    public AccountTierManager(IMinecraftSocket socket, IAuthUpdate loginNotification)
    {
        this.socket = socket;
        loginNotification.OnLogin += LoginNotification_OnLogin;
        this.loginNotification = loginNotification;
    }

    private void LoginNotification_OnLogin(object? sender, string userId)
    {
        this.userId = userId;
        socket.TryAsyncTimes(async () =>
        {
            activeSessions?.Dispose();
            activeSessions = await SelfUpdatingValue<ActiveSessions>.Create(userId, "activeSessions", () => new ActiveSessions());
            await CheckAccounttier();
            if (!socket.IsClosed)
                activeSessions.OnChange += ActiveSessions_OnChange;
        }, "get active sessions", 3);
    }

    private async Task CheckAccounttier(bool forceUpdate = false)
    {
        try
        {
            var currentTier = await GetCurrentTierWithExpire(forceUpdate);
        }
        catch (Exception e)
        {
            socket.Error(e, "Error checking account tier", JsonConvert.SerializeObject(activeSessions?.Value));
            throw;
        }
    }

    private void ActiveSessions_OnChange(ActiveSessions sessions)
    {
        socket.TryAsyncTimes(async () =>
        {
            await CheckAccounttier();
        }, "refresh tier", 1);
    }

    public async Task<AccountTier> GetCurrentCached()
    {
        if (lastTier == null || DateTime.UtcNow > expiresAt || DateTime.UtcNow >= nextTierRefresh)
            await CheckAccounttier();
        return lastTier ?? AccountTier.NONE;
    }

    public async Task RefreshTier()
    {
        InvalidateCache();
        await CheckAccounttier(true);
    }

    public void InvalidateCache()
    {
        expiresAt = DateTime.UtcNow;
    }

    public bool HasAtLeast(AccountTier tier)
    {
        return lastTier >= tier;
    }

    public async Task<(AccountTier tier, DateTime expiresAt)> GetCurrentTierWithExpire(bool forceUpdate = false)
    {
        if (Disposed)
            return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromSeconds(5));
        if (expiresAt > DateTime.UtcNow && DateTime.UtcNow < nextTierRefresh && !forceUpdate && lastTier != null)
        {
            return (lastTier.Value, expiresAt);
        }
        var currentTier = await CalculateCurrentTierWithExpire();
        if (currentTier.tier != lastTier)
        {
            OnTierChange?.Invoke(this, currentTier.tier);
        }
        (lastTier, expiresAt) = currentTier;
        nextTierRefresh = DateTime.UtcNow.AddSeconds(30);
        return currentTier;
    }
    private async Task<(AccountTier tier, DateTime expiresAt)> CalculateCurrentTierWithExpire()
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(socket.SessionInfo.McUuid))
            return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromSeconds(5));
        using var span = socket.CreateActivity("tierCalc", socket.ConSpan);
        span?.SetTag("conId", socket.SessionInfo.ConnectionId);
        var userApi = socket.GetService<PremiumService>();
        var licenseSettingsTask = socket.GetService<SettingsService>().GetCurrentValue<LicenseSetting>(userId, "licenses", () => new LicenseSetting());
        // One Payments call contains personal access and all applicable slot sources.
        // Only personal access is persisted in AccountInfo for outage fallback.
        List<OwnershipAccess> access = [];
        var expires = (socket.AccountInfo.Tier, socket.AccountInfo.ExpiresAt);
        try
        {
            access = await socket.GetService<IUserApi>().UserUserIdOwnsEntriesPostAsync(userId, socket.SessionInfo.McUuid,
                ["starter_premium", "premium", "premium_plus", "test-premium", "pre_api"]);
            var personal = access.Where(a => a.SlotId == null && a.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(GetTier).ThenByDescending(a => a.ExpiresAt).FirstOrDefault();
            expires = personal == null ? (AccountTier.NONE, DateTime.UtcNow.AddHours(3)) : (GetTier(personal), personal.ExpiresAt);
            if (socket.AccountInfo.Tier != expires.Item1
                || (personal != null && socket.AccountInfo.ExpiresAt != expires.Item2))
            {
                socket.AccountInfo.Tier = expires.Item1;
                socket.AccountInfo.ExpiresAt = expires.Item2;
                await socket.sessionLifesycle.AccountInfo.Update();
            }
        }
        catch (Exception e)
        {
            socket.Error(e, "Unable to refresh account and slot access");
            if (expires.Item2 <= DateTime.UtcNow)
                expires = (AccountTier.NONE, DateTime.UtcNow.AddSeconds(30));
        }
        IsLicense = false;
        if (activeSessions?.Value == null)
        {
            Console.WriteLine($"No active sessions for {socket.SessionInfo.McUuid} {userId}");
            span.Log("early " + expires);
            return (expires.Item1, expires.Item2);
        }
        var currentSessions = activeSessions.Value;
        var startValue = currentSessions;
        if (string.IsNullOrEmpty(currentSessions.UseAccountTierOn))
        {
            currentSessions.UseAccountTierOn = socket.SessionInfo.McUuid;
            await SyncState(startValue);
        }
        var sessions = currentSessions.Sessions;
        sessions.RemoveAll(s => string.IsNullOrEmpty(s.ConnectionId) || string.IsNullOrEmpty(s.MinecraftUuid) || s.ConnectedAt < DateTime.UtcNow - TimeSpan.FromDays(2));
        var thisSession = sessions.FirstOrDefault(s => s.ConnectionId == socket.SessionInfo.ConnectionId);
        if (thisSession == null)
        {
            thisSession = new ActiveSession()
            {
                ConnectionId = socket.SessionInfo.ConnectionId,
                ClientSessionId = socket.SessionInfo.clientSessionId,
                Tier = expires.Item1,
                ConnectedAt = DateTime.UtcNow,
                Ip = (socket as MinecraftSocket)?.ClientIp,
                LastActive = DateTime.UtcNow,
                Version = socket.Version,
                MinecraftUuid = socket.SessionInfo.McUuid,
                ClientConId = socket.SessionInfo.clientConId
            };
            if (!sessions.Any(s => s.ClientConId == thisSession.ClientConId) || socket.SessionInfo.clientConId == null)
                isNewConnection = true;
            sessions.Add(thisSession);
            Console.WriteLine($"Added session {socket.SessionInfo.ConnectionId} for {socket.SessionInfo.McUuid}");
            await Task.Delay(sessions.Count * 500);
            await SyncState(startValue);
        }
        else
        {
            if (thisSession?.Outdated ?? true)
            {
                activeSessions?.Dispose();
                var sameClient = sessions.Any(s => s.ClientSessionId == socket.SessionInfo.clientSessionId && !s.Outdated);
                if (sameClient)
                    socket.Dialog(db => db.MsgLine($"You client opened another connection, this connection is being downgraded. Your tier is used on the new connection"));
                else
                    socket.Dialog(db => db.MsgLine($"You connected from somewhere else with the same minecraft account, this connection is being downgraded. You can try to avoid this by using /cofl logout"));
                socket.sessionLifesycle.UpdateConnectionTier(AccountTier.NONE);
                span.Log(JsonConvert.SerializeObject(sessions));
                return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromMinutes(5));
            }
            if (thisSession.LastActive < DateTime.UtcNow - TimeSpan.FromSeconds(5))
            {
                thisSession.LastActive = DateTime.UtcNow;
                thisSession.Tier = socket.SessionInfo.SessionTier;
                Console.WriteLine($"Updating activity on session {socket.SessionInfo.ConnectionId} for {socket.SessionInfo.McUuid} to {thisSession.LastActive} {thisSession.Tier}");
                await SyncState(startValue);
            }
        }
        var sameMcAccount = sessions.Where(s => s.MinecraftUuid == socket.SessionInfo.McUuid).ToList();
        if (sameMcAccount.Count > 1)
        {
            var amITheLast = sameMcAccount.OrderByDescending(s => s.LastActive).ThenBy(s => s.ConnectionId).First().ConnectionId == socket.SessionInfo.ConnectionId;
            var others = sameMcAccount.Where(s => s.ConnectionId != socket.SessionInfo.ConnectionId).ToList();
            if (amITheLast)
            { // only the latest session updates the state
                foreach (var session in others.Where(o => o.LastActive < DateTime.UtcNow - TimeSpan.FromHours(2)))
                {
                    if (session.ConnectionId == socket.SessionInfo.ConnectionId)
                        continue; // don't remove self
                    sessions.Remove(session);
                }
                if (others.Where(s => !s.Outdated).Any())
                {
                    foreach (var session in others)
                    {
                        session.Outdated = true;
                    }
                    await Task.Delay(others.Count * 1000);
                    Console.WriteLine($"Removed {others.Count} other connections for {socket.SessionInfo.McUuid} from {socket.SessionInfo.ConnectionId}");
                    await SyncState(startValue);
                }
            }
        }
        if (Disposed)
        {
            activeSessions?.Dispose();
            return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromSeconds(5));
        }
        var isCurrentConOnlyCon = sessions.All(s => s.ConnectionId == socket.SessionInfo.ConnectionId || s.Outdated || s.LastActive < DateTime.UtcNow - TimeSpan.FromHours(1));
        currentSessions.UserAccountTier = access.Where(a => a.MinecraftUuid == null && a.ExpiresAt > DateTime.UtcNow)
            .Select(GetTier).DefaultIfEmpty(expires.Item1).Max();

        span.Log($"AccountTier {expires.Item1} {expires.Item2}");
        span.Log($"Sessions {JsonConvert.SerializeObject(sessions)}");
        var useEmailOnThisCon = activeSessions?.Value?.UseAccountTierOn == socket.SessionInfo.McUuid || isCurrentConOnlyCon;

        var licenseSettings = await licenseSettingsTask;
        var matchingNewLicense = licenseSettings.Licenses.OrderByDescending(l => l.Tier).FirstOrDefault(l => l.UseOnAccount == socket.SessionInfo.McUuid);

        if (matchingNewLicense != null && matchingNewLicense.Expires < DateTime.UtcNow)
        {
            var tierFor = await userApi.GetCurrentTier($"{userId}#{matchingNewLicense.VirtualId}");
            matchingNewLicense.Expires = tierFor.Item2;
            matchingNewLicense.Tier = tierFor.Item1 ?? AccountTier.NONE;
            if (ExpiresAt > DateTime.UtcNow.AddMinutes(10))
                await socket.GetService<SettingsService>().UpdateSetting(userId, "licenses", licenseSettings);
        }
        var selected = useEmailOnThisCon ? expires : (AccountTier.NONE, DateTime.UtcNow);
        if (matchingNewLicense != null && matchingNewLicense.Expires > DateTime.UtcNow
            && matchingNewLicense.Tier > selected.Item1)
        {
            selected = (matchingNewLicense.Tier, matchingNewLicense.Expires);
            IsLicense = true;
        }
        foreach (var slot in access.Where(a => a.SlotId != null && (a.MinecraftUuid != null || useEmailOnThisCon)))
        {
            if (slot.ExpiresAt > DateTime.UtcNow
                && (GetTier(slot) > selected.Item1 || (GetTier(slot) == selected.Item1 && slot.ExpiresAt > selected.Item2)))
            {
                selected = (GetTier(slot), slot.ExpiresAt);
                IsLicense = true;
            }
        }
        if (selected.Item1 > AccountTier.NONE)
        {
            thisSession.Tier = selected.Item1;
            return selected;
        }
        thisSession.Tier = AccountTier.NONE;
        if (socket.AccountInfo.ProxyOptIn)
        {
            return (AccountTier.STARTER_PREMIUM, DateTime.UtcNow + TimeSpan.FromMinutes(15));
        }
        span.Log("none");
        return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromMinutes(15));
    }

    private static AccountTier GetTier(OwnershipAccess access) => access.ProductSlug switch
    {
        "pre_api" => AccountTier.SUPER_PREMIUM,
        "premium_plus" => AccountTier.PREMIUM_PLUS,
        "premium" or "test-premium" => AccountTier.PREMIUM,
        "starter_premium" => AccountTier.STARTER_PREMIUM,
        _ => AccountTier.NONE
    };

    private static bool IsNotPreApi((AccountTier, DateTime) expires)
    {
        return expires.Item1 != AccountTier.SUPER_PREMIUM;
    }

    private async Task SyncState(ActiveSessions? startValue)
    {
        _ = socket.TryAsyncTimes(async () =>
        {
            await Task.Delay(1000);
            if (activeSessions?.Value == null || Disposed)
                return; // session closed and disposed
            if (startValue == activeSessions.Value || activeSessions.Value.Sessions.Any(s => s.ConnectionId == socket.SessionInfo.ConnectionId) != true)
                await activeSessions.Update();
            else
                Activity.Current?.Log("syncState skipped");
        }, "sync state", 1);
    }

    public string GetSessionInfo()
    {
        if (activeSessions == null)
            return "No active sessions";
        return JsonConvert.SerializeObject(activeSessions.Value, Formatting.Indented);
    }

    public bool IsConnectedFromOtherAccount(out string otherAccount, out AccountTier tier)
    {
        if (activeSessions == null)
        {
            otherAccount = "";
            tier = AccountTier.NONE;
            return false;
        }
        otherAccount = activeSessions.Value.UseAccountTierOn;
        tier = activeSessions.Value.UserAccountTier;
        return otherAccount != socket.SessionInfo.McUuid;
    }

    public async Task ChangeDefaultTo(string mcUuid)
    {
        if (activeSessions == null)
            return;
        if (activeSessions.Value == null)
            throw new CoflnetException("unavailable", "Your account could not be changed, please try again in a few seconds");
        activeSessions.Value.UseAccountTierOn = mcUuid;
        await SyncState(activeSessions.Value);
    }

    public void Dispose()
    {
        Disposed = true;
        loginNotification.OnLogin -= LoginNotification_OnLogin;
        activeSessions?.Value.Sessions.RemoveAll(s => s.ConnectionId == socket.SessionInfo.ConnectionId);
        var oldActive = activeSessions;
        activeSessions?.Update().ContinueWith(t => oldActive?.Dispose());
        activeSessions = null;
    }

    public bool IsNewConnection()
    {
        return isNewConnection;
    }
}
