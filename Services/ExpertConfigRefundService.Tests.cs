using System;
using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class ExpertConfigRefundServiceTests
{
    [Test]
    public void RevertMarksOnlyAccessForOriginalPurchase()
    {
        var configs = new OwnedConfigs
        {
            Configs =
            [
                new() { Name = "refunded", PurchaseTransactionId = 42 },
                new() { Name = "kept", PurchaseTransactionId = 43 }
            ]
        };

        Assert.That(ExpertConfigRefundService.TryGetRefundedPurchaseId(
            "revert", "config-purchase", "revert transaction 42",
            out var purchaseId), Is.True);
        Assert.That(ExpertConfigRefundService.RevokeRefundedAccess(
            configs, purchaseId, DateTime.UtcNow), Has.Count.EqualTo(1));
        Assert.That(configs.Configs, Has.Count.EqualTo(2));
        Assert.That(configs.Configs.Single(item => item.Name == "refunded")
            .RevokedAtUtc, Is.Not.Null);
        Assert.That(configs.Configs.Single(item => item.Name == "kept")
            .RevokedAtUtc, Is.Null);

        configs.RevertedPurchaseIds.Add(purchaseId);
        Assert.That(configs.RevertedPurchaseIds, Does.Contain(42));
    }

    [Test]
    public void RevokedAccessHasNoManagedUpdates()
    {
        var access = new OwnedConfigs.OwnedConfig
        {
            AccessUntilUtc = DateTime.UtcNow.AddDays(-1),
            RevokedAtUtc = DateTime.UtcNow
        };

        Assert.That(BuyConfigCommand.HasManagedUpdates(access), Is.False);
        Assert.That(access.AccessUntilUtc, Is.LessThan(DateTime.UtcNow));
    }
}
