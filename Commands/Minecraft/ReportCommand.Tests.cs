using System.Diagnostics;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class ReportCommandTests
{
    [TestCase(null, null, "unavailable: no login")]
    [TestCase("42", null, "42")]
    [TestCase(null, "43", "43")]
    [TestCase("42", "43", "42")]
    public void SettingsSnapshotPreservesConnectionsWithoutRequiringLogin(
        string sessionUserId, string accountUserId, string expectedUserId)
    {
        var flipper = new FlipperService(null, NullLogger<FlipperService>.Instance);
        var socket = new ReportSocket(flipper);
        socket.SetLifecycle(new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate(sessionUserId),
            AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { UserId = accountUserId }),
            FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings { BasedOnLBin = true })
        });
        socket.SessionInfo.VerifiedMc = true;
        var other = new Mock<IFlipConnection>();
        other.SetupGet(c => c.Id).Returns(2);
        other.SetupGet(c => c.UserId).Returns("99");
        other.SetupGet(c => c.Settings).Returns(new FlipSettings { BasedOnLBin = false });
        flipper.AddNonConnection(socket, false);
        flipper.AddNonConnection(other.Object, false);
        using var report = new Activity("report").Start();

        ReportCommand.TryAddingAllSettings(socket, report);

        var message = (string)report.Events.Single().Tags.Single(t => t.Key == "message").Value;
        var settings = JArray.Parse(message);
        Assert.That(settings.Count, Is.EqualTo(2));
        Assert.That(settings.Single(s => (string)s["UserId"] == expectedUserId)["BasedOnLBin"].Value<bool>(), Is.True);
        Assert.That(settings.Single(s => (string)s["UserId"] == "99")["BasedOnLBin"].Value<bool>(), Is.False);
        if (sessionUserId == null && accountUserId == null)
            Assert.Throws<NoLoginException>(() => { _ = socket.UserId; });
        else
            Assert.That(socket.UserId, Is.EqualTo(expectedUserId));
    }

    private sealed class ReportSocket(FlipperService flipper) : MinecraftSocket
    {
        public void SetLifecycle(ModSessionLifesycle lifecycle) => sessionLifesycle = lifecycle;

        public override T GetService<T>() => typeof(T) == typeof(FlipperService)
            ? (T)(object)flipper
            : Mock.Of<T>();
    }
}
