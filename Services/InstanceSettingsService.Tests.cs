using Coflnet.Sky.Commands.MC;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class InstanceSettingsServiceTests
{
    [Test]
    public void RowRoundTripKeepsDisableFlips()
    {
        var settings = ModSessionLifesycle.DefaultSettings;
        settings.DisableFlips = true;
        settings.MinProfit = 4242;

        var restored = InstanceSettingsService.FromRow(InstanceSettingsService.ToRow("session", settings));

        Assert.That(restored.DisableFlips, Is.True, "DisableFlips is outside the settings data contract and has to survive storage");
        Assert.That(restored.MinProfit, Is.EqualTo(4242));
    }
}
