using System;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.McConnect.Api;
using Coflnet.Sky.McConnect.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class CircumventTrackerTests
{
    [Test]
    public async Task ReservationWaitsForMcConnect()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IConnectApi>();
        api.Setup(a => a.ConnectChallengePostAsync(It.IsAny<Coflnet.Sky.McConnect.Model.Challenge>(), 0, It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        var tracker = new CircumventTracker(api.Object, NullLogger<CircumventTracker>.Instance);

        var reservation = tracker.TryReserveChallenge("auction", "minecraft", "user");

        Assert.That(reservation.IsCompleted, Is.False);
        completion.SetResult();
        Assert.That(await reservation, Is.True);
    }

    [Test]
    public async Task ConflictIsHandledWithoutReservingTheAuction()
    {
        var api = new Mock<IConnectApi>();
        api.Setup(a => a.ConnectChallengePostAsync(It.IsAny<Coflnet.Sky.McConnect.Model.Challenge>(), 0, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(409, "Conflict"));
        var tracker = new CircumventTracker(api.Object, NullLogger<CircumventTracker>.Instance);

        var result = await tracker.TryReserveChallenge("auction", "minecraft", "user");

        Assert.That(result, Is.False);
    }

    [Test]
    public void NonConflictFailuresAreNotHidden()
    {
        var api = new Mock<IConnectApi>();
        api.Setup(a => a.ConnectChallengePostAsync(It.IsAny<Coflnet.Sky.McConnect.Model.Challenge>(), 0, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(503, "Unavailable"));
        var tracker = new CircumventTracker(api.Object, NullLogger<CircumventTracker>.Instance);

        Assert.ThrowsAsync<ApiException>(() => tracker.TryReserveChallenge("auction", "minecraft", "user"));
    }

    [Test]
    public void ExcludedAuctionFilterTranslatesToSql()
    {
        var options = new DbContextOptionsBuilder<HypixelContext>()
            .UseMySql("server=localhost;database=test", new MariaDbServerVersion(new Version(10, 3)))
            .Options;
        using var context = new HypixelContext(options);

        var sql = CircumventTracker.ChallengeAuctionQuery(
            context, DateTime.UtcNow.AddMinutes(-1), new[] { "occupied" }).ToQueryString();

        Assert.That(sql, Does.Contain("occupied"));
    }
}
