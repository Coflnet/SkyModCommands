using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

#nullable enable
public interface IAccountTierManager : IDisposable
{
    bool HasAtLeast(AccountTier tier);
    Task<(AccountTier tier, DateTime expiresAt)> GetCurrentTierWithExpire();
    Task<AccountTier> GetCurrentCached();
    DateTime ExpiresAt { get; }
    string? DefaultAccount { get; }
    string GetSessionInfo();
    Task RefreshTier();
    bool IsConnectedFromOtherAccount(out string otherAccount, out AccountTier tier);
    event EventHandler<AccountTier>? OnTierChange;
    Task ChangeDefaultTo(string mcUuid);
}

public class AccountTierManager : IAccountTierManager
{
    private readonly IMinecraftSocket socket;
    private SelfUpdatingValue<ActiveSessions>? activeSessions;
    public event EventHandler<AccountTier>? OnTierChange;
    private AccountTier? lastTier;
    private DateTime expiresAt;
    private string userId;
    IAuthUpdate loginNotification;
    public DateTime ExpiresAt => expiresAt;

    public string? DefaultAccount => activeSessions?.Value.UseAccountTierOn;

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
            activeSessions.OnChange += ActiveSessions_OnChange;
        }, "get active sessions", 3);
    }

    private async Task CheckAccounttier()
    {
        try
        {
            var currentTier = await GetCurrentTierWithExpire();
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
        if (lastTier == null || DateTime.UtcNow > expiresAt)
            await CheckAccounttier();
        return lastTier ?? AccountTier.NONE;
    }

    public async Task RefreshTier()
    {
        expiresAt = DateTime.UtcNow;
        await CheckAccounttier();
    }

    public bool HasAtLeast(AccountTier tier)
    {
        return lastTier >= tier;
    }

    public async Task<(AccountTier tier, DateTime expiresAt)> GetCurrentTierWithExpire()
    {
        var currentTier = await CalculateCurrentTierWithExpire();
        if (currentTier.tier != lastTier)
        {
            OnTierChange?.Invoke(this, currentTier.tier);
        }
        (lastTier, expiresAt) = currentTier;
        return currentTier;
    }
    private async Task<(AccountTier tier, DateTime expiresAt)> CalculateCurrentTierWithExpire()
    {
        if (string.IsNullOrEmpty(userId))
            return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromSeconds(5));
        using var span = socket.CreateActivity("tierCalc", socket.ConSpan);
        var userApi = socket.GetService<PremiumService>();
        (AccountTier, DateTime) expires;
        if (expiresAt < DateTime.UtcNow.AddMinutes(5))
            expires = await userApi.GetCurrentTier(userId);
        else
        {
            expires = (lastTier ?? AccountTier.NONE, expiresAt);
        }
        if (activeSessions?.Value == null)
        {
            Console.WriteLine($"No active sessions for {socket.SessionInfo.McUuid} {userId}");
            span.Log("early " + expires);
            return (expires.Item1, expires.Item2);
        }
        var startValue = activeSessions?.Value;
        if (string.IsNullOrEmpty(activeSessions.Value.UseAccountTierOn))
        {
            activeSessions.Value.UseAccountTierOn = socket.SessionInfo.McUuid;
            await SyncState(startValue);
        }
        var sessions = activeSessions.Value.Sessions;
        sessions.RemoveAll(s => s?.ConnectionId == null || s.MinecraftUuid == null);
        if (!sessions.Any(s => s?.ConnectionId == socket.SessionInfo.ConnectionId))
        {
            sessions.Add(new ActiveSession()
            {
                ConnectionId = socket.SessionInfo.ConnectionId,
                ClientSessionId = socket.SessionInfo.clientSessionId,
                Tier = expires.Item1,
                ConnectedAt = DateTime.UtcNow,
                Ip = (socket as MinecraftSocket)?.ClientIp,
                LastActive = DateTime.UtcNow,
                Version = socket.Version,
                MinecraftUuid = socket.SessionInfo.McUuid
            });
            Console.WriteLine($"Added session {socket.SessionInfo.ConnectionId} for {socket.SessionInfo.McUuid}");
            await SyncState(startValue);
        }
        else
        {
            var session = sessions.First(s => s?.ConnectionId == socket.SessionInfo.ConnectionId);
            if (session.Outdated)
            {
                activeSessions.Dispose();
                var sameClient = sessions.Where(s => s?.ClientSessionId == socket.SessionInfo.clientSessionId && !s.Outdated).Any();
                if (sameClient)
                    socket.Dialog(db => db.MsgLine($"You client opened another connection, this connection is being downgraded. Your tier is used on the new connection"));
                else
                    socket.Dialog(db => db.MsgLine($"You connected from somewhere else with the same minecraft account, this connection is being downgraded. You can try to avoid this by using /cofl logout"));
                socket.sessionLifesycle.UpdateConnectionTier(AccountTier.NONE);
                span.Log(JsonConvert.SerializeObject(sessions));
                return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromMinutes(5));
            }
            if (session.LastActive < DateTime.UtcNow - TimeSpan.FromSeconds(5))
            {
                session.LastActive = DateTime.UtcNow;
                session.Tier = expires.Item1;
                Console.WriteLine($"Updating activity on session {socket.SessionInfo.ConnectionId} for {socket.SessionInfo.McUuid} to {session.LastActive}");
                await SyncState(startValue);
            }
        }
        var sameMcAccount = sessions.Where(s => s?.MinecraftUuid == socket.SessionInfo.McUuid).ToList();
        if (sameMcAccount.Count() > 1)
        {
            var amITheLast = sameMcAccount.OrderByDescending(s => s.LastActive).ThenBy(s => s.ConnectionId).First()!.ConnectionId == socket.SessionInfo.ConnectionId;
            var others = sameMcAccount.Where(s => s.ConnectionId != socket.SessionInfo.ConnectionId).ToList();
            if (amITheLast)
            { // only the latest session updates the state
                foreach (var session in others.Where(o => o.LastActive < DateTime.UtcNow - TimeSpan.FromMinutes(5)))
                {
                    sessions.Remove(session);
                }
                if (others.Where(s => !s.Outdated).Any())
                {
                    foreach (var session in others.Where(o => o.LastActive >= DateTime.UtcNow - TimeSpan.FromHours(1)))
                    {
                        session.Outdated = true;
                    }
                    Console.WriteLine($"Removed {others.Count} other connections for {socket.SessionInfo.McUuid} from {socket.SessionInfo.ConnectionId}");
                    await SyncState(startValue);
                }
            }
        }
        var isCurrentConOnlyCon = sessions.All(s => s == null || s.ConnectionId == socket.SessionInfo.ConnectionId || s.Outdated || s.LastActive < DateTime.UtcNow - TimeSpan.FromHours(1));
        Console.WriteLine($"Current tier: {expires.Item1} until {expires.Item2} for {socket.SessionInfo.McUuid} {socket.SessionInfo.ConnectionId} {isCurrentConOnlyCon}");
        activeSessions.Value.UserAccountTier = expires.Item1;

        // check license
        var licenses = (await socket.GetService<ILicenseApi>().ApiLicenseUUserIdGetAsync(userId)).Where(l => l.Expires > DateTime.UtcNow).ToList();
        socket.SessionInfo.LicensePoints = licenses.Sum(l => l.ProductSlug == "premium_plus" ? 5 : 1);
        var thisAccount = licenses.Where(l => l.TargetId == socket.SessionInfo.McUuid && l.Expires > DateTime.UtcNow);
        span.Log($"AccountTier {expires.Item1} {expires.Item2}");
        span.Log($"Licenses {JsonConvert.SerializeObject(licenses)}");
        span.Log($"Sessions {JsonConvert.SerializeObject(sessions)}");
        var useEmailOnThisCon = (activeSessions.Value?.UseAccountTierOn == socket.SessionInfo.McUuid || isCurrentConOnlyCon);
        if (thisAccount.Any())
        {
            Console.WriteLine($"Licenses for {socket.SessionInfo.McUuid} {JsonConvert.SerializeObject(thisAccount)}");
            var premPlus = thisAccount.FirstOrDefault(l => l.ProductSlug == "premium_plus");
            if (premPlus != null && (IsNotPreApi(expires) || !useEmailOnThisCon))
                return (AccountTier.PREMIUM_PLUS, premPlus.Expires);
        }
        if (useEmailOnThisCon && expires.Item1 > AccountTier.NONE)
        {
            return (expires.Item1, expires.Item2);
        }
        if (thisAccount.Any())
        {
            // active account may be prem+ which takes priority
            return (AccountTier.PREMIUM, thisAccount.First().Expires);
        }
        span.Log("none");
        return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromMinutes(5));
    }

    private static bool IsNotPreApi((AccountTier, DateTime) expires)
    {
        return expires.Item1 != AccountTier.SUPER_PREMIUM;
    }

    private async Task SyncState(ActiveSessions? startValue)
    {
        _ = socket.TryAsyncTimes(async () =>
        {
            await Task.Delay(1000);
            Console.WriteLine("Syncing state");
            if (startValue == activeSessions.Value)
                await activeSessions.Update();
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
        loginNotification.OnLogin -= LoginNotification_OnLogin;
        activeSessions?.Value.Sessions.RemoveAll(s => s.ConnectionId == socket.SessionInfo.ConnectionId);
        var oldActive = activeSessions;
        activeSessions?.Update().ContinueWith(t => oldActive?.Dispose());
        activeSessions = null;
    }
}