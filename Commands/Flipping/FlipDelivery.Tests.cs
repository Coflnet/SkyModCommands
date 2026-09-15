using System;
using System.Reflection;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class FlipDeliveryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedSendDoesNotPreventRetry(bool throws)
    {
        var adapter = new Mock<IModVersionAdapter>();
        if (throws)
            adapter.Setup(a => a.SendFlip(It.IsAny<FlipInstance>())).ThrowsAsync(new InvalidOperationException("send failed"));
        else
            adapter.Setup(a => a.SendFlip(It.IsAny<FlipInstance>())).ReturnsAsync(false);
        var socket = new DeliverySocket(adapter.Object);
        var processor = new FlipProcesser(socket, null, null);
        var auction = new SaveAuction { Uuid = "11111111111141118111111111111111", Tag = "TEST", FindTime = DateTime.UtcNow };
        auction.UId = AuctionService.Instance.GetId(auction.Uuid);
        var flip = new LowPricedAuction { Auction = auction, AdditionalProps = new(), Finder = LowPricedAuction.FinderType.SNIPER };
        var instance = new FlipInstance { Auction = auction };
        var send = typeof(FlipProcesser).GetMethod("SendAndTrackFlip", BindingFlags.Instance | BindingFlags.NonPublic);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var pending = (Task)send.Invoke(processor, new object[] { instance, flip, DateTime.UtcNow, false });
            if (throws)
                Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
            else
                await pending;
            Assert.That(processor.IsSent(auction.Uuid), Is.False);
        }
        adapter.Verify(a => a.SendFlip(instance), Times.Exactly(2));
        Assert.That(socket.LastSent, Is.Empty);
    }

    [Test]
    public async Task AfSkippedPurchaseDoesNotReportDelivery()
    {
        var socket = new DeliverySocket(null);
        var adapter = new StoppingAdapter(socket);
        socket.ModAdapter = adapter;
        var flip = new FlipInstance
        {
            Auction = new SaveAuction { Uuid = "11111111111141118111111111111111", ItemName = "Test", StartingBid = 1_000_000 },
            MedianPrice = 2_000_000,
            Context = new()
        };

        Assert.That(await adapter.SendFlip(flip), Is.False);
        Assert.That(adapter.CommandsSent, Is.Zero);
        adapter.StopBuying = false;
        Assert.That(await adapter.SendFlip(flip), Is.True);
        Assert.That(adapter.CommandsSent, Is.EqualTo(1));
    }

    private sealed class StoppingAdapter(MinecraftSocket socket) : AfVersionAdapter(socket)
    {
        public bool StopBuying = true;
        public int CommandsSent;
        protected override (bool skip, bool wait) ShouldStopBuying() => (StopBuying, false);
        protected override void PrintFlipCommand(FlipInstance flip, string name) => CommandsSent++;
    }

    private sealed class DeliverySocket : MinecraftSocket
    {
        public DeliverySocket(IModVersionAdapter adapter)
        {
            ModAdapter = adapter;
            sessionLifesycle = new ModSessionLifesycle(this);
            sessionLifesycle.FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings { ModSettings = new() });
        }
        public override System.Diagnostics.Activity CreateActivity(string name, System.Diagnostics.Activity parent = null) => null;
        public override T GetService<T>() where T : class => typeof(T) == typeof(IIsSold)
            ? new Mock<IIsSold>().Object as T : null;
    }
}
