using System;
using System.Net.Http;
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

    private Uri PaymentsUri() => RequiredUri(
        "PAYMENTS_BASE_URL",
        "Expert Config checkout");

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
