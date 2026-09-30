using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class ExpertConfigCheckoutClientTests
{
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body)
            });
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private static ExpertConfigCheckoutClient Client(HttpStatusCode status, string body) =>
        new(new Factory(new Handler(status, body)),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string> { ["PAYMENTS_BASE_URL"] = "http://payments.test" })
                .Build());

    [Test]
    public void UnavailableTaxQuoteBecomesFriendlyCoflnetException()
    {
        var client = Client(HttpStatusCode.BadRequest,
            "{\"Message\":\"expert_config_tax_quote_unavailable\"}");

        var ex = Assert.ThrowsAsync<CoflnetException>(() => client.GetQuote("user", 1));

        Assert.That(ex.Slug, Is.EqualTo("expert_config_tax_quote_unavailable"));
        Assert.That(ex.Message, Is.EqualTo(ExpertConfigCheckoutClient.TaxQuoteUnavailableMessage));
        Assert.That(ex.Message, Does.Not.Contain("{"));
    }

    [Test]
    public void OtherFailuresStayHttpRequestExceptions()
    {
        var client = Client(HttpStatusCode.InternalServerError, "boom");

        Assert.ThrowsAsync<HttpRequestException>(() => client.GetQuote("user", 1));
    }
}
