using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Leaderboard.Client.Api;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class BuyspeedboardCommandTests
{
    [TestCase(2024, 1, 15, 12, 34, 56, "sky-buyspeed-2024-01-15", "sky-buyspeed-2024-01-08")]
    [TestCase(2024, 1, 1, 0, 0, 0, "sky-buyspeed-2024-01-01", "sky-buyspeed-2023-12-25")]
    [TestCase(2024, 1, 8, 0, 0, 0, "sky-buyspeed-2024-01-08", "sky-buyspeed-2024-01-01")]
    [TestCase(2026, 7, 27, 10, 0, 0, "sky-buyspeed-2026-07-27", "sky-buyspeed-2026-07-20")]
    public void GetBuySpeedBoardSlugsToPurgeReturnsCurrentAndPreviousWeek(
        int year, int month, int day, int hour, int minute, int second,
        string expectedCurrent, string expectedPrevious)
    {
        var utcNow = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);

        var slugs = BuyspeedboardCommand.GetBuySpeedBoardSlugsToPurge(utcNow).ToList();

        Assert.That(slugs, Is.EqualTo(new[] { expectedCurrent, expectedPrevious }));
    }

    private static (Mock<MinecraftSocket> socket, Mock<IScoresApi> scoresApi) BuildMockSocket(string mcUuid = "some-uuid")
    {
        var mockSocket = new Mock<MinecraftSocket>();
        var settingsApi = new Mock<Coflnet.Sky.Settings.Client.Api.ISettingsApi>();
        var scoresApi = new Mock<IScoresApi>();

        mockSocket.Setup(s => s.GetService<Coflnet.Sky.Settings.Client.Api.ISettingsApi>()).Returns(settingsApi.Object);
        mockSocket.Setup(s => s.GetService<IScoresApi>()).Returns(scoresApi.Object);
        mockSocket.Setup(s => s.GetService<ILogger<LeaderboardCommand>>()).Returns(new Mock<ILogger<LeaderboardCommand>>().Object);
        mockSocket.Object.SessionInfo.McUuid = mcUuid;

        return (mockSocket, scoresApi);
    }

    [Test]
    public async Task DisableBuySpeedBoardPurgesCurrentAndPreviousWeekScoresViaTypedClient()
    {
        var (mockSocket, scoresApi) = BuildMockSocket("player-uuid");
        var expectedSlugs = BuyspeedboardCommand.GetBuySpeedBoardSlugsToPurge(DateTime.UtcNow).ToList();

        await BuyspeedboardCommand.DisableBuySpeedBoard(mockSocket.Object);

        foreach (var slug in expectedSlugs)
        {
            scoresApi.Verify(a => a.DeleteUserScoresAsync(slug, "player-uuid", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        scoresApi.Verify(a => a.DeleteUserScoresAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(expectedSlugs.Count));
    }

    [Test]
    public async Task DisableBuySpeedBoardDoesNotPurgeWhenEnabling()
    {
        var (mockSocket, scoresApi) = BuildMockSocket();

        await BuyspeedboardCommand.DisableBuySpeedBoard(mockSocket.Object, null);

        scoresApi.Verify(a => a.DeleteUserScoresAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void DisableBuySpeedBoardDoesNotThrowWhenTypedClientFails()
    {
        var (mockSocket, scoresApi) = BuildMockSocket();
        scoresApi.Setup(a => a.DeleteUserScoresAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("leaderboard service unavailable"));

        Assert.DoesNotThrowAsync(async () => await BuyspeedboardCommand.DisableBuySpeedBoard(mockSocket.Object));
    }
}
