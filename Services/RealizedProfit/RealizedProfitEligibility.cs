using System;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// Decides which users are part of the per tier stats: only users that stayed in their tier for the
/// whole measured day, so a user that just bought or is about to lose their tier doesn't skew the averages.
/// </summary>
public static class RealizedProfitEligibility
{
    public static readonly TimeSpan Margin = TimeSpan.FromDays(1);

    public static bool IsEligible(RealizedProfitSnapshot snapshot, DateTime now)
    {
        if ((AccountTier)snapshot.Tier != AccountTier.NONE)
            return snapshot.TierExpiresAt >= now + Margin;
        return HadNoRecentPaidTier(snapshot.AccountExpiresAt, now);
    }

    /// <summary>
    /// The tier manager reports NONE with a short expiry for free users, so a free session is judged by the
    /// persisted personal tier instead: it must never have been paid or have lapsed more than a day ago.
    /// </summary>
    private static bool HadNoRecentPaidTier(DateTime accountExpiresAt, DateTime now)
    {
        return accountExpiresAt == default || accountExpiresAt < now - Margin;
    }
}
