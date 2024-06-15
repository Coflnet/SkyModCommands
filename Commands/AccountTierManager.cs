using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class AccountTierManager
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
            if (activeSessions.Value.UseAccountTierOn == null)
            {
                activeSessions.Value.UseAccountTierOn = socket.SessionInfo.McUuid;
                await activeSessions.Update();
            }
            Console.WriteLine($"Current tier: {expires.Item1} until {expires.Item2} for {socket.SessionInfo.McUuid} {activeSessions.Value.UseAccountTierOn}");
            activeSessions.Value.UserAccountTier = expires.Item1;
            if (activeSessions.Value?.UseAccountTierOn == socket.SessionInfo.McUuid)
            {
                return (expires.Item1, expires.Item2);
            }
            return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromMinutes(5));
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
    }
}