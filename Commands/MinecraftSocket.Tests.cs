using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Tests;

public class MinecraftSocketTests
{
    [Test]
    [TestCase(11, 60, 11)]
    [TestCase(5, 5, 5)]
    [TestCase(51, 60, 51)]
    public async Task TestTimer(int updateIn, int countdown, int expected)
    {
        var mockSocket = new Mock<MinecraftSocket>();
        mockSocket.Setup(s=>s.GetService<FlipTrackingService>()).Returns(new FlipTrackingService(null));
        var session = new Mock<ModSessionLifesycle>(mockSocket.Object);
        session.Setup(s => s.StartTimer(It.IsAny<int>(), It.IsAny<string>()));
        var socket = new TestSocket(session.Object);
        socket.SetNextFlipTime(DateTime.UtcNow + TimeSpan.FromSeconds(updateIn));
        socket.SheduleTimer(new Commands.Shared.ModSettings() {TimerSeconds = countdown});
        await Task.Delay(10).ConfigureAwait(false);
        session.Verify(s => s.StartTimer( It.Is<double>(v=> Math.Round(v, 1) == expected), It.IsAny<string>()), Times.Once);
    }

    public class TestSocket : MinecraftSocket
    {
        public void SetNextFlipTime(DateTime time)
        {
            NextFlipTime = time;
        }
        public TestSocket(ModSessionLifesycle session)
        {
            this.sessionLifesycle = session;
        }
    }
}
