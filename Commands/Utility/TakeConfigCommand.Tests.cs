using System;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class TakeConfigCommandTests
{
    [Test]
    public void RevokeGiftedAccessRemovesTheGiftButNeverAPurchase()
    {
        var now = DateTime.UtcNow;
        var configs = new OwnedConfigs
        {
            Configs =
            [
                new()
                {
                    Name = "shared-config",
                    OwnerId = "creator-1",
                    CreatorGift = true
                },
                new()
                {
                    Name = "shared-config",
                    OwnerId = "creator-1",
                    CreatorGift = false,
                    PricePaid = 500,
                    PurchaseTransactionId = 42
                }
            ]
        };

        var (matching, revoked) = TakeConfigCommand.RevokeGiftedAccess(
            configs, "creator-1", "shared-config", now);

        Assert.Multiple(() =>
        {
            Assert.That(matching, Has.Count.EqualTo(1));
            Assert.That(revoked, Has.Count.EqualTo(1));
            Assert.That(configs.Configs.Single(c => c.CreatorGift)
                .RevokedAtUtc, Is.EqualTo(now));
            Assert.That(configs.Configs.Single(c => !c.CreatorGift)
                .RevokedAtUtc, Is.Null);
            Assert.That(configs.Configs.Single(c => !c.CreatorGift)
                .PurchaseTransactionId, Is.EqualTo(42));
        });
    }

    [Test]
    public void RevokeGiftedAccessIsANoOpWhenOnlyAPurchaseExists()
    {
        var configs = new OwnedConfigs
        {
            Configs =
            [
                new()
                {
                    Name = "paid-config",
                    OwnerId = "creator-1",
                    CreatorGift = false,
                    PricePaid = 500
                }
            ]
        };

        var (matching, revoked) = TakeConfigCommand.RevokeGiftedAccess(
            configs, "creator-1", "paid-config", DateTime.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(matching, Is.Empty);
            Assert.That(revoked, Is.Empty);
            Assert.That(configs.Configs.Single().RevokedAtUtc, Is.Null);
        });
    }
}
