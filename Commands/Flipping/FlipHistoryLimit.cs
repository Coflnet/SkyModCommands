using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Single place for how many days of flip history each tier can see in /cofl profit and /cofl flips.
/// </summary>
public static class FlipHistoryLimit
{
    public const int FreeDays = 14;
    public const int PremiumDays = 365;
    // Three years: the full flip tracking history that exists today.
    public const int PremiumPlusDays = 3 * 365;

    /// <summary>A higher tier that offers more days than the current one.</summary>
    public record Upgrade(int Days, string ProductSlug, string DisplayName);

    public static int MaxDays(AccountTier tier) => tier switch
    {
        >= AccountTier.PREMIUM_PLUS => PremiumPlusDays,
        >= AccountTier.PREMIUM => PremiumDays,
        _ => FreeDays
    };

    /// <summary>The next tier offering more days, or null when the tier already has the maximum.</summary>
    public static Upgrade NextUpgrade(AccountTier tier) => tier switch
    {
        >= AccountTier.PREMIUM_PLUS => null,
        >= AccountTier.PREMIUM => new Upgrade(PremiumPlusDays, "premium_plus", "premium plus"),
        _ => new Upgrade(PremiumDays, "premium", "premium")
    };
}
