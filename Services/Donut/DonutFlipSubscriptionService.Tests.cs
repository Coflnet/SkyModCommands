using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services.Donut;

public class DonutFlipSubscriptionServiceTests
{
    [Test]
    public async Task ClosingOldConnectionKeepsReplacementSubscription()
    {
        var userApi = new Mock<IUserApi>();
        userApi.Setup(u => u.UserUserIdOwnsProductSlugUntilGetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddDays(1));
        var service = new DonutFlipSubscriptionService(userApi.Object, new ConfigurationBuilder().Build(), NullLogger<DonutFlipSubscriptionService>.Instance);
        var oldConnection = CreateConnection();
        var replacement = CreateConnection();

        await service.RefreshSubscriptionAsync(oldConnection.Object);
        await service.RefreshSubscriptionAsync(replacement.Object);
        // the old socket closes after the replacement with the same id registered
        service.RemoveConnection(oldConnection.Object);
        await service.DeliverAsync(new LowPricedAuction
        {
            Auction = new SaveAuction(),
            AdditionalProps = new Dictionary<string, string> { { "server", DonutServerContext.Name } }
        });

        replacement.Verify(c => c.SendFlip(It.IsAny<LowPricedAuction>()), Times.Once);
    }

    private static Mock<IFlipConnection> CreateConnection()
    {
        var connection = new Mock<IFlipConnection>();
        connection.SetupGet(c => c.Id).Returns(42);
        connection.SetupGet(c => c.UserId).Returns("1");
        connection.SetupGet(c => c.GameServer).Returns(DonutServerContext.Name);
        connection.Setup(c => c.SendFlip(It.IsAny<LowPricedAuction>())).ReturnsAsync(true);
        return connection;
    }
}
