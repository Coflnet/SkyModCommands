using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.ModCommands.Services;

public sealed class ExpertConfigCheckoutClient
{
    private readonly IHttpClientFactory clients;
    private readonly IConfiguration configuration;

    public ExpertConfigCheckoutClient(
        IHttpClientFactory clients,
        IConfiguration configuration)
    {
        this.clients = clients;
        this.configuration = configuration;
    }

    public async Task<ExpertConfigQuote> GetQuote(
        string userId,
        int count)
    {
        using var response = await clients.CreateClient(nameof(ExpertConfigCheckoutClient))
            .GetAsync(new Uri(PaymentsUri(),
                $"/user/{Uri.EscapeDataString(userId)}/service/quote/config-purchase?count={count}"));
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<ExpertConfigQuote>()
            ?? throw new InvalidOperationException(
                "The Expert Config checkout returned an empty quote.");
    }

    public async Task Purchase(string userId, object request)
    {
        using var response = await clients.CreateClient(nameof(ExpertConfigCheckoutClient))
            .PostAsJsonAsync(new Uri(PaymentsUri(),
                $"/user/{Uri.EscapeDataString(userId)}/service/purchase-declared/config-purchase"),
                request);
        await EnsureSuccess(response);
    }

    public async Task WaitForConfirmation(long transactionId)
    {
        var token = configuration["PURCHASE_CONFIRMATIONS:READ_TOKEN"];
        if (token?.Length < 32)
            throw new InvalidOperationException(
                "Purchase-confirmation delivery is not configured.");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        do
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(EventsUri(),
                    $"/api/purchase-confirmations/coflcoins/{transactionId}/service_purchase"));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token);
            using var response = await clients.CreateClient(nameof(ExpertConfigCheckoutClient))
                .SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NoContent)
                return;
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new InvalidOperationException(
                    "The purchase confirmation could not be delivered. Contact support; no Config access was granted.");
            if (response.StatusCode is not HttpStatusCode.NotFound
                and not HttpStatusCode.Accepted)
                await EnsureSuccess(response);
            await Task.Delay(TimeSpan.FromSeconds(1));
        } while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException(
            "Payment was recorded, but its email confirmation is still pending. Run buyconfig again to resume; no duplicate charge or Config access will occur.");
    }

    private Uri PaymentsUri() => RequiredUri(
        "PAYMENTS_BASE_URL",
        "Expert Config checkout");

    private Uri EventsUri() => RequiredUri(
        "EVENTS_BASE_URL",
        "Purchase-confirmation delivery");

    private Uri RequiredUri(string key, string name) =>
        Uri.TryCreate(configuration[key], UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"{name} is not configured.");

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail)
                ? $"Checkout failed with HTTP {(int)response.StatusCode}."
                : detail,
            null,
            response.StatusCode);
    }
}

public sealed record ExpertConfigQuote(
    decimal CoinAmount,
    string TaxCountry,
    int VatRateBasisPoints,
    long GrossEurCents,
    long VatEurCents,
    string ConsumerRightsRegime);
