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
        ConfigureSettingsService(ownerId, configName, config);
        var (socket, lifecycle) = CreateLifecycle(userId, ownerId, configName, config.Version);
        socket.AddService((ConfigStatsService)RuntimeHelpers.GetUninitializedObject(typeof(ConfigStatsService)));

        Assert.DoesNotThrowAsync(async () => await lifecycle.FilterState.SubToConfigChanges());
    }

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

    private static void ConfigureSettingsService(string ownerId, string configName, ConfigContainer config)
    {
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi
            .Setup(api => api.GetSettingWithHttpInfoAsync(
                ownerId,
                SellConfigCommand.GetKeyFromname(configName),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject(config)));
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
