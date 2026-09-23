using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Recommendation-compliance measurement: how many bazaar order recommendations get sent per tier vs
/// how many the user actually acts on (places the order - observed when <c>SentBazaarOrderInfo.ConfirmedAt</c>
/// being set, see <see cref="BazaarOrderStateHelper.SyncSentOrdersWithUpload"/>). A tier's "ignored" rate
/// is simply (sent_total - acted_total) over a time window - no separate ignored counter is needed, and
/// none is emitted here on purpose.
/// Counters mirror the existing sky_bazaar_* style defined in BazaarFlipService.
/// </summary>
public static class BazaarRecommendationTelemetry
{
    private static readonly Prometheus.Counter RecommendationsSent = Prometheus.Metrics.CreateCounter(
        "sky_bazaar_recommendations_sent_total",
        "Count of bazaar order recommendations sent to users",
        "tier", "is_bot");

    private static readonly Prometheus.Counter RecommendationsActed = Prometheus.Metrics.CreateCounter(
        "sky_bazaar_recommendations_acted_total",
        "Count of sent bazaar order recommendations the user acted on (ConfirmedAt was set)",
        "tier", "is_bot");

    public static void RecordSent(AccountTier tier, bool isBot)
        => RecommendationsSent.WithLabels(TierLabel(tier), BotLabel(isBot)).Inc();

    public static void RecordActed(AccountTier tier, bool isBot)
        => RecommendationsActed.WithLabels(TierLabel(tier), BotLabel(isBot)).Inc();

    /// <summary>Test-facing: current value of the sent counter for a tier/bot combination.</summary>
    internal static double GetSentCount(AccountTier tier, bool isBot) => RecommendationsSent.WithLabels(TierLabel(tier), BotLabel(isBot)).Value;

    /// <summary>Test-facing: current value of the acted counter for a tier/bot combination.</summary>
    internal static double GetActedCount(AccountTier tier, bool isBot) => RecommendationsActed.WithLabels(TierLabel(tier), BotLabel(isBot)).Value;

    private static string TierLabel(AccountTier tier) => tier.ToString();
    private static string BotLabel(bool isBot) => isBot ? "true" : "false";

}
