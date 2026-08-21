using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarRecommendationTelemetryTests
{
    [Test]
    public void RecordSentIncrementsSentCounterForTierAndBotLabel()
    {
        var before = BazaarRecommendationTelemetry.GetSentCount(AccountTier.PREMIUM, true);
        var otherLabelBefore = BazaarRecommendationTelemetry.GetSentCount(AccountTier.PREMIUM, false);

        BazaarRecommendationTelemetry.RecordSent(AccountTier.PREMIUM, true);

        Assert.That(BazaarRecommendationTelemetry.GetSentCount(AccountTier.PREMIUM, true), Is.EqualTo(before + 1));
        // a different label combination must not be affected
        Assert.That(BazaarRecommendationTelemetry.GetSentCount(AccountTier.PREMIUM, false), Is.EqualTo(otherLabelBefore));
    }

    [Test]
    public void RecordActedIncrementsActedCounterForTierAndBotLabel()
    {
        var before = BazaarRecommendationTelemetry.GetActedCount(AccountTier.PREMIUM_PLUS, false);

        BazaarRecommendationTelemetry.RecordActed(AccountTier.PREMIUM_PLUS, false);

        Assert.That(BazaarRecommendationTelemetry.GetActedCount(AccountTier.PREMIUM_PLUS, false), Is.EqualTo(before + 1));
    }

    [Test]
    public void SentDoesNotImplicitlyIncrementActed()
    {
        var sentBefore = BazaarRecommendationTelemetry.GetSentCount(AccountTier.STARTER_PREMIUM, false);
        var actedBefore = BazaarRecommendationTelemetry.GetActedCount(AccountTier.STARTER_PREMIUM, false);

        BazaarRecommendationTelemetry.RecordSent(AccountTier.STARTER_PREMIUM, false);

        Assert.That(BazaarRecommendationTelemetry.GetSentCount(AccountTier.STARTER_PREMIUM, false), Is.EqualTo(sentBefore + 1));
        Assert.That(BazaarRecommendationTelemetry.GetActedCount(AccountTier.STARTER_PREMIUM, false), Is.EqualTo(actedBefore));
    }

}
