using System;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class SellConfigCommandTests
{
    [Test]
    public void LegacyPublisherListOnlyRecognizesListedMinecraftAccounts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SellConfigCommand.IsFreePublisher(
                "e7246661-de77-474f-9462-7fabf9880f60"), Is.True);
            Assert.That(SellConfigCommand.IsFreePublisher(
                "00000000000000000000000000000000"), Is.False);
        });
    }

    [Test]
    public void ManagedUpdatesEndWithoutRemovingLegacyAccess()
    {
        var expired = new OwnedConfigs.OwnedConfig
        {
            AccessUntilUtc = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc)
        };
        var legacy = new OwnedConfigs.OwnedConfig();

        Assert.Multiple(() =>
        {
            Assert.That(BuyConfigCommand.HasManagedUpdates(expired), Is.False);
            Assert.That(BuyConfigCommand.HasManagedUpdates(legacy), Is.True);
            Assert.That(legacy.AccessUntilUtc, Is.Null);
        });
    }
}
