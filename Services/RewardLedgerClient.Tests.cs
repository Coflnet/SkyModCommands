using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class RewardLedgerClientTests
{
    [Test]
    public void UsesSeparateCustomerAndFixedCreatorValuations()
    {
        var client = new RewardLedgerClient(null,
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["EXPERT_CONFIG:VALUATION_COFLCOINS"] = "1802",
                    ["EXPERT_CONFIG:VALUATION_EUR_CENTS"] = "669"
                }).Build());

        Assert.Multiple(() =>
        {
            Assert.That(client.GrossEurCents(1802), Is.EqualTo(669));
            Assert.That(client.CreatorFeeEurCents(600), Is.EqualTo(140));
            Assert.That(client.CreatorFeeEurCents(1_800), Is.EqualTo(420));
        });
    }

    [Test]
    public void CreatorFeeEurCentsRoundsDownToTheCent()
    {
        var client = new RewardLedgerClient(null,
            new ConfigurationBuilder().Build());

        Assert.Multiple(() =>
        {
            // floor(100 * 70 / 300) = floor(23.33..) = 23, never rounds up
            Assert.That(client.CreatorFeeEurCents(100), Is.EqualTo(23));
            Assert.That(client.CreatorFeeEurCents(0), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ReadsCreatorEligibilityWithTheSeparateCredential()
    {
        var handler = new Handler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler));
        var client = new RewardLedgerClient(factory.Object,
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["REFERRAL_BASE_URL"] = "https://referral.invalid",
                    ["CREATOR_ONBOARDING:READ_TOKEN"] = new string('a', 32)
                }).Build());

        var uuid = "903f634eaf90459aba2ada6d078501f2";
        var hash = new string('b', 64);
        var result = await client.GetCreatorEligibility(
            "creator 1", uuid, hash);

        Assert.Multiple(() =>
        {
            Assert.That(result.Eligible, Is.True);
            Assert.That(result.PaidPublicationReady, Is.True);
            Assert.That(handler.Path,
                Is.EqualTo(
                    $"/api/creator-onboarding/creator%201/eligibility?minecraftUuid={uuid}&agreementHash={hash}"));
            Assert.That(handler.Token, Is.EqualTo(new string('a', 32)));
        });
    }

    [Test]
    public async Task MissingCreatorMinecraftUuidIsIneligibleWithoutRequest()
    {
        var handler = new Handler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler));
        var client = new RewardLedgerClient(factory.Object,
            new ConfigurationBuilder().Build());

        var result = await client.GetCreatorEligibility("creator", null, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Eligible, Is.False);
            Assert.That(result.PaidPublicationReady, Is.False);
            Assert.That(handler.Path, Is.Null);
        });
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Path { get; private set; }
        public string Token { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri.PathAndQuery;
            Token = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"eligible\":true,\"paidPublicationReady\":true}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
