using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[NonParallelizable]
public class AccountTierManagerSlotTests
{
    [TestCase("minecraft", false, false, true)]
    [TestCase("email", true, false, true)]
    [TestCase("email", false, false, false)]
    [TestCase("minecraft", false, true, false)]
    [TestCase("legacy", false, false, true)]
    public async Task SlotAccessUsesTheLegacyLicenseFlagAndRevocationClearsIt(string assignment, bool isDefault, bool expired, bool expectedLicense)
    {
        const string userId = "42";
        const string minecraft = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var sessionInfo = new SessionInfo { McUuid = minecraft, ConnectionId = "current" };
        var accountInfo = new AccountInfo { Tier = AccountTier.NONE };
        var access = new List<OwnershipAccess>();
        var licenses = new LicenseSetting();
        if (assignment == "legacy")
            licenses.Licenses.Add(new LicenseInfo { UseOnAccount = minecraft, Tier = AccountTier.PREMIUM, Expires = DateTime.UtcNow.AddDays(1) });
        else
            access.Add(new OwnershipAccess(productSlug: "premium", expiresAt: DateTime.UtcNow.AddDays(expired ? -1 : 1),
                ownerId: "7", slotId: 9007199254740993, minecraftUuid: assignment == "minecraft" ? minecraft : null));
        var payments = new Mock<IUserApi>();
        payments.Setup(api => api.UserUserIdOwnsEntriesPostAsync(userId, minecraft, It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi.Setup(api => api.GetSettingWithHttpInfoAsync(userId, "licenses", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject(licenses)));
        var settings = new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, settingsApi.Object);
        var socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.SessionInfo).Returns(sessionInfo);
        socket.SetupGet(s => s.AccountInfo).Returns(accountInfo);
        socket.Setup(s => s.GetService<IUserApi>()).Returns(payments.Object);
        socket.Setup(s => s.GetService<SettingsService>()).Returns(settings);
        var manager = new AccountTierManager(socket.Object, Mock.Of<IAuthUpdate>());
        var sessions = new ActiveSessions
        {
            UseAccountTierOn = isDefault ? minecraft : "other",
            Sessions = [new ActiveSession { ConnectionId = "current", MinecraftUuid = minecraft, LastActive = DateTime.UtcNow },
                new ActiveSession { ConnectionId = "other", MinecraftUuid = "other", LastActive = DateTime.UtcNow }]
        };
        typeof(AccountTierManager).GetField("userId", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, userId);
        typeof(AccountTierManager).GetField("activeSessions", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(manager, SelfUpdatingValue<ActiveSessions>.CreateNoUpdate(sessions));

        var tier = await manager.GetCurrentTierWithExpire(true);
        Assert.That(manager.IsLicense, Is.EqualTo(expectedLicense));
        Assert.That(tier.tier, Is.EqualTo(expectedLicense ? AccountTier.PREMIUM : AccountTier.NONE));
        Assert.That(accountInfo.Tier, Is.EqualTo(AccountTier.NONE), "Delegated access must not be persisted as personal access.");
        payments.Verify(api => api.UserUserIdOwnsEntriesPostAsync(userId, minecraft, It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

        access.Clear();
        licenses.Licenses.Clear();
        tier = await manager.GetCurrentTierWithExpire(true);
        Assert.That(manager.IsLicense, Is.False, "Revoked slot access must not retain license delay separation.");
        Assert.That(tier.tier, Is.EqualTo(AccountTier.NONE));
    }
}
