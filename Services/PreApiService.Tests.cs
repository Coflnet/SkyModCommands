namespace Coflnet.Sky.ModCommands.Services;

using NUnit.Framework;
using Commands.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Core;
using Moq;
using Commands;
using System.Threading.Tasks;
using System;
using Coflnet.Sky.Commands.MC;

public class PreApiTests
{
    private PreApiService service;
    private FlipperService flipperService;

    [SetUp]
    public void Setup()
    {

        flipperService = new FlipperService(null, null);
        service = new PreApiService(null, flipperService, NullLogger<PreApiService>.Instance, null, null, null);
    }

    [Test]
    public async Task Test()
    {
        var flip = new LowPricedAuction()
        {
            Auction = new SaveAuction()
            {
                Uuid = "abcdef1234567890abcdef1234567890",
                Context = new() { { "cname", "test§8." } }
            },
            TargetPrice = 2_000_000
        };
        var con = new Mock<IFlipConnection>();
        con.SetupGet(c => c.UserId).Returns("5");
        LowPricedAuction lastFlip = null;
        con.Setup(c => c.SendFlip(It.IsAny<LowPricedAuction>())).Callback<LowPricedAuction>((obj) => lastFlip = obj).ReturnsAsync(true);
        var connection = con.Object;
        service.AddUser(connection, DateTime.UtcNow + TimeSpan.FromHours(1));

        await service.SendFlipCorrectly(flip, TimeSpan.Zero, connection);
        
        con.Verify(c => c.SendFlip(It.IsAny<LowPricedAuction>()), Times.Once);
        Assert.That(lastFlip.Auction.Uuid, Is.EqualTo(flip.Auction.Uuid));
        Assert.That(lastFlip.Auction.Context["cname"], Is.EqualTo("test" + McColorCodes.RED + "."));
    }

    [Test]
    public void SoldAuctionStaysSoldAfterCleanup()
    {
        // regression: the sell handler removed the sent entry and cleanup dropped every sold uuid
        // without one, so a sale was forgotten within one 45s cycle and real flips got bait-blacklisted
        var soldAt = new DateTime(2026, 11, 24, 13, 22, 46, DateTimeKind.Utc);
        service.MarkSold("757980941c1845c4a2a93a7aa79fe032", AccountTier.PREMIUM, soldAt);

        service.CleanupStaleEntries(soldAt + TimeSpan.FromSeconds(45));
        service.CleanupStaleEntries(soldAt + TimeSpan.FromMinutes(3));

        Assert.That(service.IsSold("757980941c1845c4a2a93a7aa79fe032"), Is.True);
        Assert.That(service.SoldToTier("757980941c1845c4a2a93a7aa79fe032"), Is.EqualTo(AccountTier.PREMIUM));
    }

    [Test]
    public void SoldAuctionIsForgottenAfterRetention()
    {
        var soldAt = new DateTime(2026, 11, 24, 13, 22, 46, DateTimeKind.Utc);
        service.MarkSold("757980941c1845c4a2a93a7aa79fe032", AccountTier.PREMIUM, soldAt);

        service.CleanupStaleEntries(soldAt + PreApiService.SoldRetention + TimeSpan.FromSeconds(1));

        Assert.That(service.IsSold("757980941c1845c4a2a93a7aa79fe032"), Is.False);
    }
}
