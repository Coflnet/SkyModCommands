using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class EmblemServiceTests
{
    [Test]
    public void PremiumTimeEmblemsUnlockAtEachThreshold()
    {
        var unlocked = new HashSet<string>();

        EmblemService.AddPremiumTimeEmblems(unlocked, TimeSpan.FromDays(365 * 2), TimeSpan.FromDays(365));

        Assert.That(unlocked, Does.Contain(Emblems.PremiumSixMonths));
        Assert.That(unlocked, Does.Contain(Emblems.PremiumOneYear));
        Assert.That(unlocked, Does.Contain(Emblems.PremiumTwoYears));
        Assert.That(unlocked, Does.Not.Contain(Emblems.PremiumThreeYears));
        Assert.That(unlocked, Does.Contain(Emblems.PremiumPlusSixMonths));
        Assert.That(unlocked, Does.Contain(Emblems.PremiumPlusOneYear));
        Assert.That(unlocked, Does.Not.Contain(Emblems.PremiumPlusTwoYears));
    }

    [Test]
    public void PremiumTimeOnlyCountsPeriodsForTheRequestedUser()
    {
        var start = new DateTime(2024, 1, 1);
        var periods = new[]
        {
            new OwnershipTimeFrame("42", start, start.AddDays(100)),
            new OwnershipTimeFrame("7", start, start.AddDays(200)),
            new OwnershipTimeFrame("42", start, start.AddDays(80)),
            new OwnershipTimeFrame("42", start, start.AddDays(-1)),
        };

        Assert.That(EmblemService.SumOwnedTime(periods, "42"), Is.EqualTo(TimeSpan.FromDays(180)));
        Assert.That(EmblemService.CountPurchases(periods, "42"), Is.EqualTo(2));
    }

    [Test]
    public void PreApiEmblemsUnlockAtPurchaseThresholds()
    {
        var unlocked = new HashSet<string>();

        EmblemService.AddPreApiPurchaseEmblems(unlocked, 10);

        Assert.That(unlocked, Does.Contain(Emblems.PreApiOnePurchase));
        Assert.That(unlocked, Does.Contain(Emblems.PreApiFivePurchases));
        Assert.That(unlocked, Does.Contain(Emblems.PreApiTenPurchases));
        Assert.That(unlocked, Does.Not.Contain(Emblems.PreApiTwentyPurchases));

        EmblemService.AddPreApiPurchaseEmblems(unlocked, 20);
        Assert.That(unlocked, Does.Contain(Emblems.PreApiTwentyPurchases));
    }

    [TestCase("384a029294fc445e863f2c42fe9709cb", true, true)]
    [TestCase("384a029294fc445e863f2c42fe9709cb", false, false)]
    [TestCase("00000000000000000000000000000000", true, false)]
    public async Task ModeratorEmblemIsOnlyAvailableToModerators(string minecraftUuid, bool verified, bool expected)
    {
        var socket = new Mock<MinecraftSocket>();
        socket.Object.SessionInfo.McUuid = minecraftUuid;
        socket.Object.SessionInfo.VerifiedMc = verified;
        socket.Setup(s => s.GetService<ModeratorService>()).Returns(new ModeratorService());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string>("PLAYERSTATE_BASE_URL", "http://player-state") })
            .Build();
        var service = new EmblemService(
            new HttpClient(new EmptyAchievementsHandler()),
            config,
            NullLogger<EmblemService>.Instance);

        var unlocked = await service.GetUnlockedForSocket(socket.Object);

        Assert.That(unlocked.Contains(Emblems.Moderator), Is.EqualTo(expected));
    }

    private sealed class EmptyAchievementsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        }
    }
}
