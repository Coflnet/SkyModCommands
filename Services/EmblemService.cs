using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Mod side access to the achievement state that lives in the player state service (SkyUserState).
/// Reads a players unlocked achievements (to show their emblems) and requests unlocks for actions that
/// happen inside the mod backend (e.g. lowballing).
///
/// Unlocks are sent as an <see cref="UpdateMessage.UpdateKind.Achievement"/> update through the state
/// pipeline (NOT an http call). The pipeline is partitioned by player id, so the message is processed on
/// the exact replica holding the players live state - an http call could hit any of the replicas and the
/// change would be lost to a concurrent save on the owning one.
/// </summary>
public class EmblemService
{
    private readonly HttpClient http;
    private readonly string baseUrl;
    private readonly string premiumSlug;
    private readonly string premiumPlusSlug;
    private readonly string preApiSlug;
    private readonly ILogger<EmblemService> logger;
    private readonly ConcurrentDictionary<string, (HashSet<string> set, DateTime at)> cache = new();
    private readonly ConcurrentDictionary<string, (TimeSpan premium, TimeSpan premiumPlus, int preApiPurchases, DateTime at)> purchaseStatsCache = new();
    private static readonly TimeSpan cacheTtl = TimeSpan.FromMinutes(1);

    public EmblemService(HttpClient http, IConfiguration config, ILogger<EmblemService> logger)
    {
        this.http = http;
        this.baseUrl = config["PLAYERSTATE_BASE_URL"];
        this.premiumSlug = config["PRODUCTS:PREMIUM"] ?? "premium";
        this.premiumPlusSlug = config["PRODUCTS:PREMIUM_PLUS"] ?? "premium_plus";
        this.preApiSlug = config["PRODUCTS:PRE_API"] ?? "pre_api";
        this.logger = logger;
    }

    /// <summary>
    /// Returns the set of achievement ids the player has unlocked. Cached for a short time per player.
    /// </summary>
    public async Task<HashSet<string>> GetUnlocked(string playerId, bool forceRefresh = false)
    {
        if (!forceRefresh && cache.TryGetValue(playerId, out var cached) && cached.at + cacheTtl > DateTime.UtcNow)
            return cached.set;
        try
        {
            var json = await http.GetStringAsync($"{baseUrl}/PlayerState/{Uri.EscapeDataString(playerId)}/achievements");
            var set = JsonConvert.DeserializeObject<HashSet<string>>(json) ?? new HashSet<string>();
            cache[playerId] = (set, DateTime.UtcNow);
            return set;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load unlocked achievements for {player}", playerId);
            // fall back to whatever we had cached, otherwise empty - never break the calling command
            if (cache.TryGetValue(playerId, out var fallback))
                return fallback.set;
            return new HashSet<string>();
        }
    }

    /// <summary>
    /// Requests an achievement unlock for the player behind the socket and, if we believe it is new,
    /// tells them about it and auto-equips the emblem when they don't have one shown yet.
    /// Safe to call on every action - once the achievement is known unlocked it is a cheap no-op.
    /// </summary>
    public async Task TriggerUnlock(MinecraftSocket socket, string achievementId)
    {
        try
        {
            var playerId = socket.SessionInfo.McUuid;
            if (string.IsNullOrEmpty(playerId))
                return;
            var known = await GetUnlocked(playerId);
            if (known.Contains(achievementId))
                return; // already unlocked, nothing to do

            socket.GetService<IStateUpdateService>().Produce(playerId, new UpdateMessage
            {
                Kind = UpdateMessage.UpdateKind.Achievement,
                AchievementId = achievementId,
                ReceivedAt = DateTime.UtcNow
            });
            known.Add(achievementId);
            cache[playerId] = (known, DateTime.UtcNow);

            var emblem = Emblems.GetById(achievementId);
            if (emblem == null)
                return;
            socket.Dialog(db => db
                .MsgLine($"{McColorCodes.GOLD}{McColorCodes.BOLD}Emblem unlocked! {emblem.Symbol} {McColorCodes.YELLOW}{emblem.Name}", null, emblem.Description)
                .CoflCommand<EmblemCommand>($"{McColorCodes.GRAY}[Click to view and equip your emblems]", "", "Open the emblem menu"));
            // auto-equip if the user has none shown yet
            if (socket.AccountInfo != null && string.IsNullOrEmpty(socket.AccountInfo.Emblem))
            {
                socket.AccountInfo.Emblem = emblem.Symbol;
                await socket.sessionLifesycle.AccountInfo.Update();
                socket.Dialog(db => db.MsgLine($"{McColorCodes.GRAY}It now shows in front of your chat messages. Change it with {McColorCodes.AQUA}/cofl emblem"));
            }
        }
        catch (Exception e)
        {
            socket.Error(e, "unlocking achievement " + achievementId);
        }
    }

    /// <summary>
    /// The account-age emblems and the minimum account age each one needs. Unlike the achievement backed
    /// emblems these are not "unlocked" by anyone at a point in time - they are derived on the fly from the
    /// account creation date, so a player simply has them once their account is old enough. One year is
    /// approximated as 365 days; a day or two of drift doesn't matter for a loyalty badge.
    /// </summary>
    private static readonly (TimeSpan minAge, string emblemId)[] ageEmblems =
    {
        (TimeSpan.FromDays(365 * 1), Emblems.OneYearVeteran),
        (TimeSpan.FromDays(365 * 3), Emblems.ThreeYearVeteran),
        (TimeSpan.FromDays(365 * 5), Emblems.FiveYearVeteran),
    };

    private static readonly (TimeSpan minTime, string emblemId)[] premiumTimeEmblems =
    {
        (TimeSpan.FromDays(180), Emblems.PremiumSixMonths),
        (TimeSpan.FromDays(365), Emblems.PremiumOneYear),
        (TimeSpan.FromDays(365 * 2), Emblems.PremiumTwoYears),
        (TimeSpan.FromDays(365 * 3), Emblems.PremiumThreeYears),
    };

    private static readonly (TimeSpan minTime, string emblemId)[] premiumPlusTimeEmblems =
    {
        (TimeSpan.FromDays(180), Emblems.PremiumPlusSixMonths),
        (TimeSpan.FromDays(365), Emblems.PremiumPlusOneYear),
        (TimeSpan.FromDays(365 * 2), Emblems.PremiumPlusTwoYears),
        (TimeSpan.FromDays(365 * 3), Emblems.PremiumPlusThreeYears),
    };

    private static readonly (int minPurchases, string emblemId)[] preApiPurchaseEmblems =
    {
        (1, Emblems.PreApiOnePurchase),
        (5, Emblems.PreApiFivePurchases),
        (10, Emblems.PreApiTenPurchases),
        (20, Emblems.PreApiTwentyPurchases),
    };

    /// <summary>
    /// The unlocked emblem ids for the player behind the socket: the achievement backed ones from the state
    /// service, plus the derived emblems the account currently qualifies for through account age, purchase
    /// history, or moderator status. This is the set the emblem command lists and validates equips against.
    /// </summary>
    public async Task<HashSet<string>> GetUnlockedForSocket(MinecraftSocket socket, bool forceRefresh = false)
    {
        var unlockedTask = GetUnlocked(socket.SessionInfo.McUuid, forceRefresh);
        var userTask = Task.Run(() =>
        {
            return int.TryParse(socket.AccountInfo?.UserId, out var userId)
                && UserService.Instance.TryGetUserById(userId, out var user) ? user : null;
        });
        var user = await userTask;
        var purchaseStatsTask = user == null ? null : GetPurchaseStats(socket, user);
        var set = new HashSet<string>(await unlockedTask);
        if (socket.GetService<ModeratorService>().IsModerator(socket))
            set.Add(Emblems.Moderator);
        if (user != null)
        {
            var age = DateTime.UtcNow - user.CreatedAt;
            foreach (var (minAge, emblemId) in ageEmblems)
                if (age >= minAge)
                    set.Add(emblemId);

            var purchaseStats = await purchaseStatsTask;
            AddPremiumTimeEmblems(set, purchaseStats.premium, purchaseStats.premiumPlus);
            AddPreApiPurchaseEmblems(set, purchaseStats.preApiPurchases);
        }
        return set;
    }

    private async Task<(TimeSpan premium, TimeSpan premiumPlus, int preApiPurchases)> GetPurchaseStats(MinecraftSocket socket, GoogleUser user)
    {
        var userId = user.Id.ToString();
        if (purchaseStatsCache.TryGetValue(userId, out var cached) && cached.at + cacheTtl > DateTime.UtcNow)
            return (cached.premium, cached.premiumPlus, cached.preApiPurchases);

        try
        {
            var productsApi = socket.GetService<IProductsApi>();
            var now = DateTime.UtcNow;
            var premiumTask = productsApi.ProductsServiceServiceSlugOwnedGetAsync(premiumSlug, user.CreatedAt, now);
            var premiumPlusTask = productsApi.ProductsServiceServiceSlugOwnedGetAsync(premiumPlusSlug, user.CreatedAt, now);
            var preApiTask = productsApi.ProductsServiceServiceSlugOwnedGetAsync(preApiSlug, user.CreatedAt, now);
            await Task.WhenAll(premiumTask, premiumPlusTask, preApiTask);
            var result = (
                premium: SumOwnedTime(await premiumTask, userId),
                premiumPlus: SumOwnedTime(await premiumPlusTask, userId),
                preApiPurchases: CountPurchases(await preApiTask, userId),
                at: DateTime.UtcNow);
            purchaseStatsCache[userId] = result;
            return (result.premium, result.premiumPlus, result.preApiPurchases);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load emblem purchase stats for {user}", userId);
            if (purchaseStatsCache.TryGetValue(userId, out var fallback))
                return (fallback.premium, fallback.premiumPlus, fallback.preApiPurchases);
            return (TimeSpan.Zero, TimeSpan.Zero, 0);
        }
    }

    internal static TimeSpan SumOwnedTime(IEnumerable<OwnershipTimeFrame> timeFrames, string userId)
    {
        return TimeSpan.FromTicks(timeFrames
            .Where(frame => frame.UserId == userId && frame.End > frame.Start)
            .Sum(frame => (frame.End - frame.Start).Ticks));
    }

    internal static int CountPurchases(IEnumerable<OwnershipTimeFrame> timeFrames, string userId)
    {
        return timeFrames.Count(frame => frame.UserId == userId && frame.End > frame.Start);
    }

    internal static void AddPremiumTimeEmblems(HashSet<string> set, TimeSpan premium, TimeSpan premiumPlus)
    {
        foreach (var (minTime, emblemId) in premiumTimeEmblems)
            if (premium >= minTime)
                set.Add(emblemId);
        foreach (var (minTime, emblemId) in premiumPlusTimeEmblems)
            if (premiumPlus >= minTime)
                set.Add(emblemId);
    }

    internal static void AddPreApiPurchaseEmblems(HashSet<string> set, int purchaseCount)
    {
        foreach (var (minPurchases, emblemId) in preApiPurchaseEmblems)
            if (purchaseCount >= minPurchases)
                set.Add(emblemId);
    }
}
