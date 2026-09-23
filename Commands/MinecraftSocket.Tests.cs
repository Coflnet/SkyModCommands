using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Tests;

public class MinecraftSocketTests
{
    [TestCase(11, 60, 11)]
    [TestCase(5, 5, 5)]
    [TestCase(51, 60, 51)]
    public async Task TestTimer(int updateIn, int countdown, int expected)
    {
        DiHandler.ResetProvider();
        DiHandler.OverrideService<IAhActive, IAhActive>(new Mock<IAhActive>().Object);
        var mockSocket = new Mock<MinecraftSocket>();
        var config = new Mock<IConfiguration>();
        mockSocket.Setup(s => s.GetService<FlipTrackingService>())
            .Returns(new FlipTrackingService(null, null, config.Object, null, null, null, null, null, null, null));
        var session = new Mock<ModSessionLifesycle>(mockSocket.Object);
        session.Setup(s => s.StartTimer(It.IsAny<int>(), It.IsAny<string>()));
        var socket = new TestSocket(session.Object);
        socket.SetNextFlipTime(DateTime.UtcNow + TimeSpan.FromSeconds(updateIn));
        socket.ScheduleTimer(new ModSettings() { TimerSeconds = countdown });
        await Task.Delay(10).ConfigureAwait(false);
        session.Verify(s => s.StartTimer(It.Is<double>(v => Math.Round(v, 1) == expected), It.IsAny<string>()), Times.Once);
    }

    [Test]
    public void Compare()
    {
        var dictionary = new ConcurrentDictionary<IFlipConnection, DateTime>();
        dictionary.TryAdd(new MinecraftSocket(), DateTime.UtcNow);
        dictionary.TryAdd(new MinecraftSocket(), DateTime.UtcNow);
        Assert.That(dictionary.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task StoresExactCommandForPremiumPlusRetry()
    {
        var session = new Mock<ModSessionLifesycle>(new Mock<MinecraftSocket>().Object);
        var socket = new TestSocket(session.Object);

        await socket.InvokeCommand(
            new Response("premiumPlusTest", JsonConvert.SerializeObject("sort attribute 2")),
            new PremiumPlusTestCommand());

        Assert.That(socket.TakePremiumPlusRetryCommand(), Is.EqualTo("/cofl premiumplustest sort attribute 2"));
        Assert.That(socket.TakePremiumPlusRetryCommand(), Is.Null);
    }

    [Test]
    public void RegistersProxySyncOnlyOnce()
    {
        var socket = new MinecraftSocket();
        var registrations = 0;

        Parallel.For(0, 20, _ =>
        {
            if (socket.TryRegisterProxySync())
                Interlocked.Increment(ref registrations);
        });

        Assert.That(registrations, Is.EqualTo(1));
    }

    private sealed class PremiumPlusTestCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.StoreCurrentCommandForPremiumPlusRetry();
            return Task.CompletedTask;
        }
    }

    public class TestSocket : MinecraftSocket
    {
        public override bool IsClosed => false;
        public void SetNextFlipTime(DateTime time)
        {
            NextFlipTime = time;
        }
        public TestSocket(ModSessionLifesycle session)
        {
            sessionLifesycle = session;
        }
    }
}

public class FlipStreamTests
{
    //[Test]
    public async Task LoadTest()
    {
        IServiceCollection collection = new ServiceCollection();
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(new Dictionary<string, string>() { { "API_BASE_URL", "http://no" } });
        collection.AddSingleton<IConfiguration>((a) => builder.Build());
        collection.AddLogging();
        collection.AddCoflService();
        var provider = collection.BuildServiceProvider();
        var socket = new MinecraftSocket();
        socket.SetLifecycleVersion("1.4.2-Alpha");
        socket.sessionLifesycle.FlipSettings = await SelfUpdatingValue<FlipSettings>.CreateNoUpdate(() => new FlipSettings());
        socket.sessionLifesycle.AccountInfo = await SelfUpdatingValue<AccountInfo>.CreateNoUpdate(() => new AccountInfo() { });
        provider.GetRequiredService<FlipperService>().AddConnection(socket);

        //_ = Task.Run(async () =>
        // {
        for (int i = 0; i < 1000; i++)
        {
            await provider.GetRequiredService<FlipperService>().DeliverLowPricedAuction(new Core.LowPricedAuction() { Auction = new(), Finder = Core.LowPricedAuction.FinderType.SNIPER });
        }
        // });
        await Task.Delay(10);
        Assert.That(socket.TopBlocked.Count, Is.GreaterThanOrEqualTo(500));

    }
}
