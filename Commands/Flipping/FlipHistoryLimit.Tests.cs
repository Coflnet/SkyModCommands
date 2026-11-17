using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class FlipHistoryLimitTests
{
    [TestCase(AccountTier.NONE, 14)]
    [TestCase(AccountTier.STARTER_PREMIUM, 14)]
    [TestCase(AccountTier.PREMIUM, 365)]
    [TestCase(AccountTier.PREMIUM_PLUS, 1095)]
    [TestCase(AccountTier.SUPER_PREMIUM, 1095)]
    public void MaxDays_MapsTierToDays(AccountTier tier, int days)
    {
        FlipHistoryLimit.MaxDays(tier).Should().Be(days);
    }

    [TestCase(AccountTier.NONE, 365, "premium", "premium")]
    [TestCase(AccountTier.STARTER_PREMIUM, 365, "premium", "premium")]
    [TestCase(AccountTier.PREMIUM, 1095, "premium_plus", "premium plus")]
    public void NextUpgrade_PointsToNextTierWithMoreDays(AccountTier tier, int days, string slug, string name)
    {
        var upgrade = FlipHistoryLimit.NextUpgrade(tier);

        upgrade.Days.Should().Be(days);
        upgrade.ProductSlug.Should().Be(slug);
        upgrade.DisplayName.Should().Be(name);
    }

    [TestCase(AccountTier.PREMIUM_PLUS)]
    [TestCase(AccountTier.SUPER_PREMIUM)]
    public void NextUpgrade_IsNullAtTopTier(AccountTier tier)
    {
        FlipHistoryLimit.NextUpgrade(tier).Should().BeNull();
    }

    [TestCase(AccountTier.NONE)]
    [TestCase(AccountTier.STARTER_PREMIUM)]
    public void DeniedMessage_FreeTiersLimitedTo14(AccountTier tier)
    {
        FlipsCommand.DeniedMessage(14, tier).Should().BeNull();
        FlipsCommand.DeniedMessage(15, tier).Should().Be("You need to be premium to see more than 14 days of flips");
    }

    [Test]
    public void DeniedMessage_PremiumLimitedTo365()
    {
        FlipsCommand.DeniedMessage(365, AccountTier.PREMIUM).Should().BeNull();
        FlipsCommand.DeniedMessage(366, AccountTier.PREMIUM).Should().Be("You need to be premium plus to see more than 365 days of flips");
    }

    [TestCase(AccountTier.PREMIUM_PLUS)]
    [TestCase(AccountTier.SUPER_PREMIUM)]
    public void DeniedMessage_PremiumPlusHasNoCap(AccountTier tier)
    {
        FlipsCommand.DeniedMessage(100000, tier).Should().BeNull();
    }
}
