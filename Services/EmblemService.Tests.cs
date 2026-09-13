using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
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
    public async Task PurchaseStatsUseBoundedUserTransactionsAndSlugDurations()
    {
        var transactionApi = new Mock<ITransactionApi>();
        transactionApi.Setup(api => api.TransactionUUserIdGetAsync("42", 0, 2000, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalTransaction>
            {
                new(productId: "premium"),
                new(productId: "premium-day"),
                new(productId: "premium_plus-weeks"),
                new(productId: "premium_plus-months"),
                new(productId: "l_premium-year"),
                new(productId: "l_prem_plus-quarter"),
                new(productId: "pre_api"),
                new(productId: "pre_api"),
                new(productId: "starter_premium")
            });
        var socket = new Mock<MinecraftSocket>();
        socket.Setup(s => s.GetService<ITransactionApi>()).Returns(transactionApi.Object);
        var service = CreateService();

        var result = await service.GetPurchaseStats(socket.Object, new GoogleUser { Id = 42 });

        Assert.That(result.premium, Is.EqualTo(TimeSpan.FromDays(591)));
        Assert.That(result.premiumPlus, Is.EqualTo(TimeSpan.FromDays(195)));
        Assert.That(result.preApiPurchases, Is.EqualTo(2));
        transactionApi.Verify(api => api.TransactionUUserIdGetAsync("42", 0, 2000, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ReportedBulkPurchasesUnlockSixMonthPremium()
    {
        var transactionApi = new Mock<ITransactionApi>();
        transactionApi.Setup(api => api.TransactionUUserIdGetAsync("45", 0, 2000, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalTransaction>
            {
                new(id: "371866", productId: "premium", amount: -1800),
                new(id: "371865", productId: "premium", amount: -5400),
                new(id: "371864", productId: "premium", amount: -5400),
                new(id: "371863", productId: "premium", amount: -5400),
                new(id: "370474", productId: "premium_plus", amount: -2700),
                new(id: "370469", productId: "premium", amount: -1800)
            });
        var productsApi = new Mock<IProductsApi>();
        productsApi.Setup(api => api.ProductsPProductSlugGetAsync("premium", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseableProduct(cost: 1800));
        productsApi.Setup(api => api.ProductsPProductSlugGetAsync("premium_plus", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseableProduct(cost: 2700));
        var socket = new Mock<MinecraftSocket>();
        socket.Setup(s => s.GetService<ITransactionApi>()).Returns(transactionApi.Object);
        socket.Setup(s => s.GetService<IProductsApi>()).Returns(productsApi.Object);
        var service = CreateService();

        var result = await service.GetPurchaseStats(socket.Object, new GoogleUser { Id = 45 });
        var unlocked = new HashSet<string>();
        EmblemService.AddPremiumTimeEmblems(unlocked, result.premium, result.premiumPlus);

        Assert.That(result.premium, Is.EqualTo(TimeSpan.FromDays(337)));
        Assert.That(result.premiumPlus, Is.EqualTo(TimeSpan.FromDays(7)));
        Assert.That(unlocked, Is.EquivalentTo(new[] { Emblems.PremiumSixMonths }));
        Assert.That(await service.GetPurchaseStats(socket.Object, new GoogleUser { Id = 45 }), Is.EqualTo(result));
        productsApi.Verify(api => api.ProductsPProductSlugGetAsync("premium", 0, It.IsAny<CancellationToken>()), Times.Once);
        productsApi.Verify(api => api.ProductsPProductSlugGetAsync("premium_plus", 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("premium", 1800, -5400, 90)]
    [TestCase("premium_plus", 2700, -8100, 21)]
    [TestCase("premium_plus-weeks", 9000, -27000, 84)]
    [TestCase("premium_plus-months", 21600, -64800, 231)]
    [TestCase("premium-old", 1200, -3600, 90)]
    [TestCase("premium", 1800, -900, 30)]
    [TestCase("premium", 1800, -2700, 30)]
    [TestCase("premium", 0, -5400, 30)]
    [TestCase("premium", 1800, 0, 30)]
    public async Task BulkPurchaseDurationsUseProductPrice(string slug, double cost, double amount, int days)
    {
        var transactionApi = new Mock<ITransactionApi>();
        transactionApi.Setup(api => api.TransactionUUserIdGetAsync("46", 0, 2000, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalTransaction> { new(productId: slug, amount: amount) });
        var productsApi = new Mock<IProductsApi>();
        productsApi.Setup(api => api.ProductsPProductSlugGetAsync(slug, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseableProduct(cost: cost));
        var socket = new Mock<MinecraftSocket>();
        socket.Setup(s => s.GetService<ITransactionApi>()).Returns(transactionApi.Object);
        socket.Setup(s => s.GetService<IProductsApi>()).Returns(productsApi.Object);

        var result = await CreateService().GetPurchaseStats(socket.Object, new GoogleUser { Id = 46 });

        Assert.That(result.premium, Is.EqualTo(TimeSpan.FromDays(days)));
        Assert.That(result.premiumPlus, Is.EqualTo(slug.StartsWith("premium_plus") ? TimeSpan.FromDays(days) : TimeSpan.Zero));
        var unlocked = new HashSet<string>();
        EmblemService.AddPremiumTimeEmblems(unlocked, result.premium, result.premiumPlus);
        Assert.That(unlocked.Contains(Emblems.PremiumPlusSixMonths), Is.EqualTo(result.premiumPlus.TotalDays >= 180));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnavailableProductPricePreservesOtherPurchaseStats(bool throws)
    {
        var transactionApi = new Mock<ITransactionApi>();
        transactionApi.Setup(api => api.TransactionUUserIdGetAsync("47", 0, 2000, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalTransaction>
            {
                new(productId: "premium", amount: -5400),
                new(productId: "premium_plus", amount: -8100),
                new(productId: "pre_api", amount: -1400)
            });
        var productsApi = new Mock<IProductsApi>();
        if (throws)
            productsApi.Setup(api => api.ProductsPProductSlugGetAsync("premium", 0, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Product lookup unavailable"));
        productsApi.Setup(api => api.ProductsPProductSlugGetAsync("premium_plus", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseableProduct(cost: 2700));
        var socket = new Mock<MinecraftSocket>();
        socket.Setup(s => s.GetService<ITransactionApi>()).Returns(transactionApi.Object);
        socket.Setup(s => s.GetService<IProductsApi>()).Returns(productsApi.Object);

        var result = await CreateService().GetPurchaseStats(socket.Object, new GoogleUser { Id = 47 });

        Assert.That(result.premium, Is.EqualTo(TimeSpan.FromDays(51)));
        Assert.That(result.premiumPlus, Is.EqualTo(TimeSpan.FromDays(21)));
        Assert.That(result.preApiPurchases, Is.EqualTo(1));
        productsApi.Verify(api => api.ProductsPProductSlugGetAsync("pre_api", 0, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task TransactionCapSpeculativelyUnlocksAllPremiumEmblems()
    {
        var transactionApi = new Mock<ITransactionApi>();
        transactionApi.Setup(api => api.TransactionUUserIdGetAsync("43", 0, 2000, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Repeat(new ExternalTransaction(productId: "transfer"), 2000).ToList());
        var socket = new Mock<MinecraftSocket>();
        socket.Setup(s => s.GetService<ITransactionApi>()).Returns(transactionApi.Object);
        var service = CreateService();

        var result = await service.GetPurchaseStats(socket.Object, new GoogleUser { Id = 43 });
        var unlocked = new HashSet<string>();
        EmblemService.AddPremiumTimeEmblems(unlocked, result.premium, result.premiumPlus);

        var premiumEmblems = Emblems.All.Where(e => e.Category is Emblems.PremiumCategory or Emblems.PremiumPlusCategory);
        Assert.That(premiumEmblems.All(e => unlocked.Contains(e.Id)), Is.True);
    }

    [TestCase("premium", 2592000)]
    [TestCase("premium-day", 86400)]
    [TestCase("premium-derpy", 432000)]
    [TestCase("premium_plus", 604800)]
    [TestCase("premium_plus-day", 86400)]
    [TestCase("premium_plus-hour", 3600)]
    [TestCase("premium_plus-week", 604800)]
    [TestCase("premium_plus-weeks", 2419200)]
    [TestCase("premium_plus-months", 6652800)]
    [TestCase("premium_plus-100", 8640000)]
    [TestCase("l_premium", 2430000)]
    [TestCase("l_premium-quarter", 7776000)]
    [TestCase("l_premium-year", 31536000)]
    [TestCase("l_prem_plus", 2430000)]
    [TestCase("l_prem_plus-quarter", 7776000)]
    [TestCase("l_prem_plus-year", 31536000)]
    [TestCase("test-premium", 259200)]
    public async Task ProvisionedPremiumDurationsAreDerivedFromSlug(string productSlug, int expectedSeconds)
    {
        var transactionApi = new Mock<ITransactionApi>();
        transactionApi.Setup(api => api.TransactionUUserIdGetAsync("44", 0, 2000, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalTransaction> { new(productId: productSlug) });
        var socket = new Mock<MinecraftSocket>();
        socket.Setup(s => s.GetService<ITransactionApi>()).Returns(transactionApi.Object);

        var result = await CreateService().GetPurchaseStats(socket.Object, new GoogleUser { Id = 44 });

        Assert.That(result.premium, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
        var isPremiumPlus = productSlug.StartsWith("premium_plus") || productSlug.StartsWith("l_prem_plus");
        Assert.That(result.premiumPlus, Is.EqualTo(isPremiumPlus ? TimeSpan.FromSeconds(expectedSeconds) : TimeSpan.Zero));
    }

    private static EmblemService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("PLAYERSTATE_BASE_URL", "http://player-state")
            })
            .Build();
        return new EmblemService(new HttpClient(new EmptyAchievementsHandler()), config, NullLogger<EmblemService>.Instance);
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
