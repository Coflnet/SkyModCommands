using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class AccountTierManager : IDisposable
    {
#nullable enable
        private readonly IMinecraftSocket socket;
        private SelfUpdatingValue<ActiveSessions>? activeSessions;
        public event EventHandler<AccountTier>? OnTierChange;
        private AccountTier? lastTier;
        private DateTime expiresAt;
        private string userId;

        public DateTime ExpiresAt => expiresAt;

        public AccountTierManager(IMinecraftSocket socket, IAuthUpdate loginNotification)
        {
            this.socket = socket;
            loginNotification.OnLogin += LoginNotification_OnLogin;
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
            }, "get active sessions", 2);
        }

        private async Task CheckAccounttier()
        {
            var currentTier = await GetCurrentTierWithExpire();
            if (currentTier.tier != lastTier)
            {
                OnTierChange?.Invoke(this, currentTier.tier);
            }
            (lastTier, expiresAt) = currentTier;
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
            await CheckAccounttier();
        }

        public bool HasAtLeast(AccountTier tier)
        {
            return lastTier >= tier;
        }

        public async Task<(AccountTier tier, DateTime expiresAt)> GetCurrentTierWithExpire()
        {
            if (activeSessions?.Value == null)
                return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromMinutes(5));
            // check license
            var licenses = await socket.GetService<ILicenseApi>().ApiLicenseUUserIdGetAsync(userId);
            var thisAccount = licenses.Where(l => l.TargetId == socket.SessionInfo.McUuid && l.Expires > DateTime.UtcNow);
            if (thisAccount.Any())
            {
                var premPlus = thisAccount.FirstOrDefault(l => l.ProductSlug == "premium_plus");
                if (premPlus != null)
                    return (AccountTier.PREMIUM_PLUS, premPlus.Expires);
                return (AccountTier.PREMIUM, thisAccount.First().Expires);
            }
            var userApi = socket.GetService<PremiumService>();
            var expiresTask = userApi.GetCurrentTier(userId);
            var expires = await expiresTask;
            if (string.IsNullOrEmpty(activeSessions.Value.UseAccountTierOn))
            {
                activeSessions.Value.UseAccountTierOn = socket.SessionInfo.McUuid;
                await activeSessions.Update();
            }
            var sessions = activeSessions.Value.Sessions;
            if (!sessions.Any(s => s.ConnectionId == socket.SessionInfo.ConnectionId))
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
                await activeSessions.Update();
            }
            else
            {
                var session = sessions.First(s => s.ConnectionId == socket.SessionInfo.ConnectionId);
                session.LastActive = DateTime.UtcNow;
                session.Tier = expires.Item1;
                if (session.LastActive < DateTime.Now - TimeSpan.FromMinutes(5))
                    await activeSessions.Update();
            }
            var isCurrentConOnlyCon = sessions.All(s => s.ConnectionId == socket.SessionInfo.ConnectionId || s.LastActive < DateTime.UtcNow - TimeSpan.FromHours(1));
            Console.WriteLine($"Current tier: {expires.Item1} until {expires.Item2} for {socket.SessionInfo.McUuid} {activeSessions.Value.UseAccountTierOn} {isCurrentConOnlyCon}");
            activeSessions.Value.UserAccountTier = expires.Item1;
            if (activeSessions.Value?.UseAccountTierOn == socket.SessionInfo.McUuid || isCurrentConOnlyCon)
            {
                return (expires.Item1, expires.Item2);
            }
            return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromMinutes(5));
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
            activeSessions.Value.UseAccountTierOn = mcUuid;
            await activeSessions.Update();
        }

        public void Dispose()
        {
            activeSessions?.Value.Sessions.RemoveAll(s => s.ConnectionId == socket.SessionInfo.ConnectionId);
            activeSessions?.Update().ContinueWith(t => activeSessions?.Dispose());
            activeSessions = null;
        }
    }
}