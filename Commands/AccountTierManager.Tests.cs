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
using AwesomeAssertions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.MC;

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

    [Test]
    public async Task DisposeWhileLoadingActiveSessionsDoesNotKeepTheSubscription()
    {
        var gate = new TaskCompletionSource<Coflnet.Sky.Settings.Client.Client.ApiResponse<string>>();
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi.Setup(api => api.GetSettingWithHttpInfoAsync("42", "activeSessions", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, settingsApi.Object));
        var socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.SessionInfo).Returns(new SessionInfo { ConnectionId = "c" });
        Task running = Task.CompletedTask;
        socket.Setup(s => s.TryAsyncTimes(It.IsAny<Func<Task>>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((Func<Task> action, string _, int _) =>
            {
                running = action();
                return Task.CompletedTask.ConfigureAwait(false);
            });
        var auth = new Mock<IAuthUpdate>();
        var manager = new AccountTierManager(socket.Object, auth.Object);
        auth.Raise(a => a.OnLogin += null, this, "42");

        manager.Dispose();
        gate.SetResult(new(HttpStatusCode.OK, JsonConvert.SerializeObject(new ActiveSessions())));
        await running.WaitAsync(TimeSpan.FromSeconds(5));

        typeof(AccountTierManager).GetField("activeSessions", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(manager).Should().BeNull();
    }

    [Test]
    public async Task InvalidateCacheKeepsTheLastCalculatedExpiry()
    {
        var socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.SessionInfo).Returns(new SessionInfo { ConnectionId = "c" });
        var manager = new AccountTierManager(socket.Object, Mock.Of<IAuthUpdate>());
        var (_, expiresAt) = await manager.GetCurrentTierWithExpire(true);

        manager.InvalidateCache();

        manager.ExpiresAt.Should().BeBefore(expiresAt);
        manager.LastCalculatedExpiresAt.Should().Be(expiresAt);
    }

    [Test]
    public async Task ConcurrentLoginsLeaveOnlyTheLatestActiveSessionsSubscribed()
    {
        var gates = new[] { new TaskCompletionSource<Coflnet.Sky.Settings.Client.Client.ApiResponse<string>>(),
            new TaskCompletionSource<Coflnet.Sky.Settings.Client.Client.ApiResponse<string>>() };
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi.SetupSequence(api => api.GetSettingWithHttpInfoAsync("42", "activeSessions", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(gates[0].Task).Returns(gates[1].Task);
        DiHandler.OverrideService<SettingsService, SettingsService>(
            new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, settingsApi.Object));
        var socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.SessionInfo).Returns(new SessionInfo { ConnectionId = "c" });
        var running = new List<Task>();
        socket.Setup(s => s.TryAsyncTimes(It.IsAny<Func<Task>>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((Func<Task> action, string _, int _) =>
            {
                running.Add(action());
                return Task.CompletedTask.ConfigureAwait(false);
            });
        var auth = new Mock<IAuthUpdate>();
        var manager = new AccountTierManager(socket.Object, auth.Object);
        auth.Raise(a => a.OnLogin += null, this, "42");
        auth.Raise(a => a.OnLogin += null, this, "42");
        var field = typeof(AccountTierManager).GetField("activeSessions", BindingFlags.NonPublic | BindingFlags.Instance);
        var body = JsonConvert.SerializeObject(new ActiveSessions());

        gates[0].SetResult(new(HttpStatusCode.OK, body));
        await running[0].WaitAsync(TimeSpan.FromSeconds(5));
        var first = (SelfUpdatingValue<ActiveSessions>)field.GetValue(manager);
        gates[1].SetResult(new(HttpStatusCode.OK, body));
        await running[1].WaitAsync(TimeSpan.FromSeconds(5));

        var latest = (SelfUpdatingValue<ActiveSessions>)field.GetValue(manager);
        latest.Should().NotBeSameAs(first);
        latest.Value.Should().NotBeNull();
        first.Value.Should().BeNull("the replaced value has to be disposed");
    }
}
