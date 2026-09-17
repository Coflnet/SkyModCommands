using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Models;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarInstantBuyTests
{
    private sealed class Backend(HttpStatusCode status) : HttpMessageHandler
    {
        public List<JObject> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/OrderBook/instant-buy"));
            Requests.Add(JObject.Parse(await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent("true") };
        }
    }

    [TestCase(HttpStatusCode.OK)]
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public async Task ForwardsImmediateChatWithCanonicalTagsAndDoesNotRetry(HttpStatusCode status)
    {
        using var backend = new Backend(status);
        using var client = new HttpClient(backend) { BaseAddress = new Uri("http://bazaar/") };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient("BazaarOrders")).Returns(client);
        var socket = new Mock<IMinecraftSocket>();
        socket.Setup(s => s.GetService<IHttpClientFactory>()).Returns(clients.Object);
        var items = new Mock<IItemsApi>();
        socket.Setup(s => s.GetService<IItemsApi>()).Returns(items.Object);
        socket.SetupGet(s => s.SessionInfo).Returns(new SessionInfo { BazaarDisplayState = new BazaarOrderSnapshot {
            ItemNames = new() { ["GILL_MEMBRANE"] = "Gill Membrane" }
        } });
        var time = DateTime.UtcNow;
        await BazaarInstantBuy.Forward(socket.Object, new[] {
            "[Bazaar] Executing instant buy...",
            "§6[Bazaar] §fBought §a1x §fGill Membrane for §681.9 coins!",
            "[Bazaar] Bought 1x Gill Membrane for 81.9 coins!",
            "[Bazaar] Bought 1,024x Beetle Shard for 1,234.5 coins!",
            "[VIP] OtherPlayer: [Bazaar] Bought 1x Gill Membrane for 81.9 coins!",
            "[Bazaar] Claimed 256x Gill Membrane worth 1,817.6 coins bought for 7.1 each!"
        }, time);
        Assert.That(backend.Requests, Has.Count.EqualTo(3));
        Assert.That((string)backend.Requests[0]["itemTag"], Is.EqualTo("GILL_MEMBRANE"));
        Assert.That((double)backend.Requests[0]["coins"], Is.EqualTo(81.9));
        Assert.That((DateTime)backend.Requests[0]["timestamp"], Is.EqualTo(time.AddTicks(1)));
        Assert.That((DateTime)backend.Requests[1]["timestamp"], Is.EqualTo(time.AddTicks(2)));
        Assert.That((string)backend.Requests[2]["itemTag"], Is.EqualTo("SHARD_CROPEETLE"));
        Assert.That((int)backend.Requests[2]["amount"], Is.EqualTo(1024));
        Assert.That((double)backend.Requests[2]["coins"], Is.EqualTo(1234.5));
        items.Verify(i => i.ItemsSearchTermGetAsync(It.IsAny<string>(), 0, default), Times.Never);
    }
}
