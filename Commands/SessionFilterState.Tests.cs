using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Settings.Client.Api;
using Coflnet.Sky.Settings.Client.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using AwesomeAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class SessionFilterStateTests
{
    [Test]
    [NonParallelizable]
    public void SubToConfigChangesUsesSessionUserIdWhenAccountInfoIsMissing()
    {
        const string userId = "42";
        const string ownerId = "17";
        const string configName = "test";
        var config = CreateConfig(ownerId, configName);
        var (socket, lifecycle) = CreateLifecycle(userId, ownerId, configName, config.Version);
        ConfigureSettingsService(socket.UserId, ownerId, configName, config);
        socket.AddService((ConfigStatsService)RuntimeHelpers.GetUninitializedObject(typeof(ConfigStatsService)));

        Assert.DoesNotThrowAsync(async () => await lifecycle.FilterState.SubToConfigChanges());
    }

    [Test]
    [NonParallelizable]
    public async Task SubToConfigChangesReadsTheCurrentAccountSettingsAfterTheyWereReplaced()
    {
        var (socket, lifecycle) = CreateLifecycle("42", "17", "test", 3);
        var old = lifecycle.AccountSettings;
        // empty on purpose: only reading the current instance fills it
        var replacement = SelfUpdatingValue<AccountSettings>.CreateNoUpdate((AccountSettings)null);
        var replaced = false;
        ConfigureSettingsService(socket.UserId, "17", "test", CreateConfig("17", "test"), () =>
        {
            if (replaced)
                return;
            replaced = true;
            // SubToSettings ran again while the network calls were pending
            ModSessionLifesycle.ReplaceValue(ref lifecycle.AccountSettings, replacement);
        });
        socket.AddService((ConfigStatsService)RuntimeHelpers.GetUninitializedObject(typeof(ConfigStatsService)));

        await lifecycle.FilterState.SubToConfigChanges();

        old.Value.Should().BeNull("the replaced instance is disposed and must not be written to again");
        lifecycle.AccountSettings.Should().BeSameAs(replacement);
        replacement.Value.Should().NotBeNull();
    }

    [Test]
    [NonParallelizable]
    public async Task LoadedConfigArrivingAfterTheSessionEndedIsNotKept()
    {
        var (socket, lifecycle) = CreateLifecycle("42", "17", "test", 3);
        var gate = new TaskCompletionSource<ApiResponse<string>>();
        var requested = new TaskCompletionSource();
        ConfigureSettingsService(socket.UserId, "17", "test", CreateConfig("17", "test"), configResponse: () =>
        {
            requested.TrySetResult();
            return gate.Task;
        });
        socket.AddService((ConfigStatsService)RuntimeHelpers.GetUninitializedObject(typeof(ConfigStatsService)));

        var run = lifecycle.FilterState.SubToConfigChanges();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifecycle.Dispose();
        gate.SetResult(ConfigResponse(CreateConfig("17", "test")));
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        lifecycle.FilterState.LoadedConfig.Should().BeNull("FilterState.Dispose already ran, a value stored now is never released");
    }

    [Test]
    [NonParallelizable]
    public async Task OverlappingConfigSubscriptionsDisposeTheReplacedValue()
    {
        var (socket, lifecycle) = CreateLifecycle("42", "17", "test", 3);
        var gates = new[] { new TaskCompletionSource<ApiResponse<string>>(), new TaskCompletionSource<ApiResponse<string>>() };
        var calls = 0;
        ConfigureSettingsService(socket.UserId, "17", "test", CreateConfig("17", "test"), configResponse: () => gates[calls++].Task);
        socket.AddService((ConfigStatsService)RuntimeHelpers.GetUninitializedObject(typeof(ConfigStatsService)));

        // a signed-out login runs the subscription from UserId.OnChange and from the login prompt
        var first = lifecycle.FilterState.SubToConfigChanges();
        var second = lifecycle.FilterState.SubToConfigChanges();
        calls.Should().Be(2, "both runs wait for the config at the same time");
        gates[0].SetResult(ConfigResponse(CreateConfig("17", "test")));
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        var firstValue = lifecycle.FilterState.LoadedConfig;
        gates[1].SetResult(ConfigResponse(CreateConfig("17", "test")));
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        firstValue.Should().NotBeNull();
        lifecycle.FilterState.LoadedConfig.Should().NotBeSameAs(firstValue);
        lifecycle.FilterState.LoadedConfig.Value.Should().NotBeNull();
        firstValue.Value.Should().BeNull("the replaced value would keep its subscription and prompt a second time");
    }

    private static ApiResponse<string> ConfigResponse(ConfigContainer config)
        => new(HttpStatusCode.OK, JsonConvert.SerializeObject(config));

    private static ConfigContainer CreateConfig(string ownerId, string configName)
    {
        return new ConfigContainer
        {
            Name = configName,
            OwnerId = ownerId,
            Version = 3,
            Settings = new FlipSettings
            {
                BlackList = [],
                WhiteList = []
            }
        };
    }

    private static void ConfigureSettingsService(string userId, string ownerId, string configName, ConfigContainer config, Action onOwnedConfigs = null,
        Func<Task<ApiResponse<string>>> configResponse = null)
    {
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi
            .Setup(api => api.GetSettingWithHttpInfoAsync(
                userId,
                "owned_configs",
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                onOwnedConfigs?.Invoke();
                return Task.FromResult(new ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject(new OwnedConfigs
                {
                    Configs = [new OwnedConfigs.OwnedConfig { OwnerId = ownerId, Name = configName, Version = config.Version }]
                })));
            });
        settingsApi
            .Setup(api => api.GetSettingWithHttpInfoAsync(
                ownerId,
                SellConfigCommand.GetKeyFromname(configName),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(configResponse ?? (() => Task.FromResult(ConfigResponse(config))));
        var configuration = new Mock<IConfiguration>();
        var settingsService = new SettingsService(configuration.Object, NullLogger<SettingsService>.Instance, settingsApi.Object);
        DiHandler.OverrideService<SettingsService, SettingsService>(settingsService);
    }

    private static (TestSocket socket, ModSessionLifesycle lifecycle) CreateLifecycle(
        string userId,
        string ownerId,
        string configName,
        int configVersion)
    {
        var socket = new TestSocket();
        var lifecycle = new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate(userId),
            AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate((AccountInfo)null),
            AccountSettings = SelfUpdatingValue<AccountSettings>.CreateNoUpdate(new AccountSettings
            {
                LoadedConfig = new OwnedConfigs.OwnedConfig
                {
                    Name = configName,
                    OwnerId = ownerId,
                    Version = configVersion
                }
            }),
            FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings
            {
                BlackList = [],
                WhiteList = []
            }),
            TierManager = Mock.Of<IAccountTierManager>(manager => manager.IsLicense == false)
        };
        socket.SetLifecycle(lifecycle);
        return (socket, lifecycle);
    }

    private sealed class TestSocket : MinecraftSocket
    {
        private readonly Dictionary<Type, object> services = new();

        public override bool IsClosed => false;

        public override Activity CreateActivity(string name, Activity parent = null)
        {
            return null;
        }

        public void SetLifecycle(ModSessionLifesycle lifecycle)
        {
            sessionLifesycle = lifecycle;
        }

        public void AddService<T>(T service) where T : class
        {
            services[typeof(T)] = service;
        }

        public override T GetService<T>()
        {
            return services.TryGetValue(typeof(T), out var service)
                ? (T)service
                : Mock.Of<T>();
        }
    }
}
