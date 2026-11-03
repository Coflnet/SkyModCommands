#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// What a session knew about its user when it disconnected, enough to compute the
/// realized profit of the last day without touching the (disposed) session again.
/// </summary>
/// <param name="UserId">The user id of the logged in user</param>
/// <param name="Tier">Tier of the session as int, the cassandra driver cannot map enums</param>
/// <param name="TierExpiresAt">Expiry of the tier that applied to the session</param>
/// <param name="AccountExpiresAt">Expiry of the personal tier stored in the account info, default if never paid</param>
/// <param name="McUuids">All minecraft accounts of the user</param>
public record RealizedProfitSnapshot(
    string UserId,
    int Tier,
    DateTime TierExpiresAt,
    DateTime AccountExpiresAt,
    IReadOnlyList<string> McUuids)
{
    /// <summary>
    /// Builds the snapshot from cached session values only, never loads anything.
    /// Returns null when nobody is logged in or the user has no minecraft account.
    /// </summary>
    public static RealizedProfitSnapshot? Create(string? userId, AccountTier tier, DateTime tierExpiresAt,
        AccountInfo? accountInfo, SessionInfo? sessionInfo)
    {
        if (string.IsNullOrEmpty(userId))
            return null;
        var uuids = CollectUuids(accountInfo, sessionInfo);
        if (uuids.Count == 0)
            return null;
        return new RealizedProfitSnapshot(userId, (int)tier, tierExpiresAt, accountInfo?.ExpiresAt ?? default, uuids);
    }

    private static List<string> CollectUuids(AccountInfo? accountInfo, SessionInfo? sessionInfo)
    {
        IEnumerable<string> all = [sessionInfo?.McUuid ?? "", .. sessionInfo?.MinecraftUuids ?? [], .. accountInfo?.McIds ?? []];
        return all.Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
    }
}
