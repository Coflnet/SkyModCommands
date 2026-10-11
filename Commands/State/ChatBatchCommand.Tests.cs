using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Proxy.Client.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[NonParallelizable]
public class ChatBatchCommandTests
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

    [TestCase("Bid of 197 coins placed for Stick!", 197)]
    [TestCase("Bid of 1,250,000 coins placed for Stick!", 1250000)]
    public void ParsesBidAmount(string line, int expected)
    {
        Assert.That(ChatBatchCommand.TryParseBid(line, out var bid), Is.True);
        Assert.That(bid, Is.EqualTo(expected));
    }

    [TestCase("Bid of coins")]
    [TestCase("Bid of  coins placed")]
    [TestCase("Bid of 99999999999 coins placed")]
    [TestCase("")]
    public void SkipsBidLinesWithoutUsableAmount(string line)
    {
        Assert.That(ChatBatchCommand.TryParseBid(line, out _), Is.False);
    }

    [TestCase("You purchased ◆ Hot Rune I for 1 coins!", "◆ Hot Rune I", 1)]
    [TestCase("You purchased Aspect of the End for 1,500,000 coins!", "Aspect of the End", 1500000)]
    [TestCase("You purchased Stick for 1 coin!", "Stick", 1)]
    public void ParsesPurchaseLines(string line, string name, long price)
    {
        Assert.That(PreApiService.TryParsePurchase(line, out var parsedName, out var parsedPrice), Is.True);
        Assert.That(parsedName, Is.EqualTo(name));
        Assert.That(parsedPrice, Is.EqualTo(price));
    }

    [TestCase("You purchased Booster Cookie!")]
    [TestCase("You purchased Stick for coins!")]
    [TestCase("You purchased Stick for 5 gems")]
    [TestCase("You purchased")]
    public void SkipsPurchaseLinesWithoutCoinAmount(string line)
    {
        Assert.That(PreApiService.TryParsePurchase(line, out _, out _), Is.False);
        var service = new PreApiService(null, new FlipperService(null, null), NullLogger<PreApiService>.Instance, null, null, null);
        Assert.DoesNotThrow(() => service.PurchaseMessage(new TestSocket(), line));
    }

    [Test]
    public async Task FailingLineDoesNotDropTheRestOfTheBatch()
    {
        var socket = CreateSocket();
        socket.SessionInfo.VerificationBidAuctioneer = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        socket.SessionInfo.VerificationBidAmount = 100;
        // GetService<IBaseApi> throws in the test socket, so the bid line fails
        var batch = new List<string> { "Bid of 100 coins placed for Stick!", "Profile ID: abc-123" };

        await new ChatBatchCommand().Execute(socket, JsonConvert.SerializeObject(batch));

        Assert.That(socket.SessionInfo.ProfileId, Is.EqualTo("abc-123"));
    }

    [Test]
    public async Task UnresolvableSellerNameDoesNotCallProxy()
    {
        var socket = CreateSocket();
        var baseApi = new Mock<IBaseApi>(MockBehavior.Strict);
        socket.Services[typeof(IBaseApi)] = baseApi.Object;
        socket.ResolvedUuid = "";
        var batch = new List<string> { "You claimed Stick from Nobody's auction!" };

        await new ChatBatchCommand().Execute(socket, JsonConvert.SerializeObject(batch));

        baseApi.VerifyNoOtherCalls();
    }

    private static TestSocket CreateSocket()
    {
        var socket = new TestSocket();
        socket.SetLifecycle(new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate("42"),
            AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { UserId = "42" })
        });
        return socket;
    }

    private sealed class TestSocket : MinecraftSocket
    {
        public Dictionary<Type, object> Services { get; } = new();
        public string ResolvedUuid { get; set; } = "";

        public void SetLifecycle(ModSessionLifesycle lifecycle) => sessionLifesycle = lifecycle;

        public override Task<string> GetPlayerUuid(string name, bool blockError = false) => Task.FromResult(ResolvedUuid);

        public override T GetService<T>()
        {
            if (Services.TryGetValue(typeof(T), out var service))
                return (T)service;
            if (typeof(T) == typeof(IBaseApi))
                throw new InvalidOperationException("no base api in test");
            if (typeof(T) == typeof(PreApiService))
                return (T)(object)new PreApiService(null, new FlipperService(null, null), NullLogger<PreApiService>.Instance, null, null, null);
            return Mock.Of<T>();
        }
    }
}
