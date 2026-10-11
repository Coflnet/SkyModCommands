using System;
using System.IO;
using System.Reflection;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[NonParallelizable]
public class DisposeSafetyTests
{
    private bool devModeBefore;

    [SetUp]
    public void DisableDevMode()
    {
        devModeBefore = MinecraftSocket.IsDevMode;
        MinecraftSocket.IsDevMode = false;
    }

    [TearDown]
    public void RestoreDevMode() => MinecraftSocket.IsDevMode = devModeBefore;

    [Test]
    public void TierManagerDisposeSurvivesSessionsThatNeverLoaded()
    {
        var auth = new Mock<IAuthUpdate>();
        var socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.SessionInfo).Returns((SessionInfo)null);
        var manager = new AccountTierManager(socket.Object, auth.Object);
        // socket closed before the active sessions were loaded: holder exists but has no value
        typeof(AccountTierManager).GetField("activeSessions", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(manager, SelfUpdatingValue<ActiveSessions>.CreateNoUpdate((ActiveSessions)null));

        Assert.DoesNotThrow(() => manager.Dispose());
        Assert.DoesNotThrow(() => manager.Dispose());
    }

    [Test]
    public void TierManagerDisposeSurvivesNeverLoggedInSocket()
    {
        var manager = new AccountTierManager(Mock.Of<IMinecraftSocket>(), Mock.Of<IAuthUpdate>());

        Assert.DoesNotThrow(() => manager.Dispose());
    }

    [Test]
    public void LifecycleDisposeRunsRemainingCleanupWhenTierManagerThrows()
    {
        var socket = new DisposeSocket();
        var lifecycle = new ModSessionLifesycle(socket);
        socket.SetLifecycle(lifecycle);
        var tier = new Mock<IAccountTierManager>();
        tier.Setup(t => t.Dispose()).Throws<NullReferenceException>();
        lifecycle.TierManager = tier.Object;
        lifecycle.PingTimer = new System.Threading.Timer(_ => { });

        Assert.DoesNotThrow(() => lifecycle.Dispose());
        tier.Verify(t => t.Dispose(), Times.Once);
    }

    [Test]
    public void LifecycleDisposeDoesNotUnregisterProxyForSocketWithoutUser()
    {
        var socket = new DisposeSocket();
        var lifecycle = new ModSessionLifesycle(socket);
        socket.SetLifecycle(lifecycle);
        lifecycle.TierManager = Mock.Of<IAccountTierManager>();
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            lifecycle.Dispose();
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.That(output.ToString(), Does.Not.Contain("Failed to unregister socket from proxy service"));
    }

    private sealed class DisposeSocket : MinecraftSocket
    {
        public void SetLifecycle(ModSessionLifesycle lifecycle) => sessionLifesycle = lifecycle;

        public override T GetService<T>()
        {
            if (typeof(T) == typeof(ProxyService))
                return (T)(object)new ProxyService(null, Mock.Of<IConfiguration>(), NullLogger<ProxyService>.Instance, new System.Net.Http.HttpClient());
            return Mock.Of<T>();
        }
    }
}
