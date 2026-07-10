using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
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
    private readonly ILogger<EmblemService> logger;
    private readonly ConcurrentDictionary<string, (HashSet<string> set, DateTime at)> cache = new();
    private static readonly TimeSpan cacheTtl = TimeSpan.FromMinutes(1);

    public EmblemService(HttpClient http, IConfiguration config, ILogger<EmblemService> logger)
    {
        this.http = http;
        this.baseUrl = config["PLAYERSTATE_BASE_URL"];
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

    /// <summary>
    /// The unlocked emblem ids for the player behind the socket: the achievement backed ones from the state
    /// service, plus the account-age emblems the account currently qualifies for (derived from the account
    /// creation date). This is the set the emblem command lists and validates equips against.
    /// </summary>
    public async Task<HashSet<string>> GetUnlockedForSocket(MinecraftSocket socket, bool forceRefresh = false)
    {
        var set = new HashSet<string>(await GetUnlocked(socket.SessionInfo.McUuid, forceRefresh));
        var info = socket.AccountInfo;
        if (info != null)
        {
            var age = DateTime.UtcNow - info.CreatedAt;
            foreach (var (minAge, emblemId) in ageEmblems)
                if (age >= minAge)
                    set.Add(emblemId);
        }
        return set;
    }
}
