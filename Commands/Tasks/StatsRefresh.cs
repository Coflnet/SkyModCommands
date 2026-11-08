using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Makes the mod open the "Stats &amp; Equipment" menu (<c>/stats</c>) now and then so SkyPlayerState
/// can read the current gear and stats and use them as reference for the task estimates.
/// Stats with the same equipment change slowly, so once a week is enough.
/// </summary>
public static class StatsRefresh
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(5);
    private const string Command = "/stats";

    // Throttle in memory per player name: no persistent per-account flag store exists. It survives
    // reconnects but not a restart, the stored-stats check above covers that case.
    private static readonly ConcurrentDictionary<string, DateTime> lastTriggered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stats are due when they were never read or the last read is older than <see cref="MaxAge"/></summary>
    public static bool IsDue(ExtractedInfo info, DateTime now)
    {
        if (info == null || info.PlayerStats == null || info.PlayerStats.Count == 0)
            return true;
        return info.PlayerStatsAt == default || now - info.PlayerStatsAt > MaxAge;
    }

    /// <summary>Claims the weekly slot of a player, false when it was already used within <see cref="MaxAge"/></summary>
    public static bool TryClaimThrottle(string playerName, DateTime now)
    {
        if (string.IsNullOrEmpty(playerName))
            return false;
        var claimed = false;
        lastTriggered.AddOrUpdate(playerName, _ => { claimed = true; return now; }, (_, last) =>
        {
            claimed = now - last > MaxAge;
            return claimed ? now : last;
        });
        return claimed;
    }

    /// <summary>
    /// Runs <c>/stats</c> on the client when a refresh is due. Never throws: a failing state lookup
    /// only skips the refresh.
    /// </summary>
    public static async Task RefreshIfDue(MinecraftSocket socket)
    {
        try
        {
            await RefreshIfDue(socket, DateTime.UtcNow);
        }
        catch (Exception e)
        {
            Log(socket)?.LogDebug(e, "skipping stats refresh");
        }
    }

    internal static async Task RefreshIfDue(MinecraftSocket socket, DateTime now)
    {
        var name = socket.SessionInfo?.McName;
        if (string.IsNullOrEmpty(name))
            return;
        var info = await LoadState(socket, name);
        if (!IsDue(info, now) || !TryClaimThrottle(name, now))
            return;
        socket.Dialog(db => db.MsgLine($"{McColorCodes.GRAY}Briefly opening your stats menu to refresh your gear and stats for task estimates (about once a week)."));
        socket.ExecuteCommand(Command);
    }

    private static async Task<ExtractedInfo> LoadState(MinecraftSocket socket, string name)
    {
        using var cts = new CancellationTokenSource(LookupTimeout);
        return await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdExtractedGetAsync(name, cancellationToken: cts.Token);
    }

    private static ILogger Log(MinecraftSocket socket)
    {
        try { return socket.GetService<ILogger<MinecraftSocket>>(); }
        catch (Exception) { return null; }
    }

    internal static void ResetThrottle() => lastTriggered.Clear();
}
