using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.ModCommands.Services;

public sealed class RewardLedgerClient
{
    private const int CreatorFeeCoflCoins = 300;
    private const int CreatorFeeEurCentsPerBlock = 70;
    private readonly IHttpClientFactory clients;
    private readonly IConfiguration configuration;

    public RewardLedgerClient(
        IHttpClientFactory clients,
        IConfiguration configuration)
    {
        this.clients = clients;
        this.configuration = configuration;
    }

    public long GrossEurCents(decimal coflCoins) => Convert(
        coflCoins,
        configuration.GetValue<int>("EXPERT_CONFIG:VALUATION_EUR_CENTS"),
        configuration.GetValue<int>("EXPERT_CONFIG:VALUATION_COFLCOINS"));

    /// <summary>
    /// EUR 0.70 per 300 listed CoflCoins, proportional for other amounts
    /// and rounded DOWN to the cent (never up, unlike <see cref="Convert"/>
    /// which is used for the customer-facing <see cref="GrossEurCents"/>).
    /// </summary>
    public long CreatorFeeEurCents(int listedCoflCoins)
    {
        if (listedCoflCoins < 0)
            throw new InvalidOperationException(
                "Expert Config EUR valuation is not configured.");
        return (long)Math.Floor(listedCoflCoins
            * CreatorFeeEurCentsPerBlock / (decimal)CreatorFeeCoflCoins);
    }

    public string DescribeValuation(int coflCoins)
    {
        _ = Connection();
        return $"{coflCoins} CoflCoins are valued at EUR {GrossEurCents(coflCoins) / 100m:0.00} "
            + $"gross. The fixed creator fee is EUR {CreatorFeeEurCents(coflCoins) / 100m:0.00} (EUR 0.70 per {CreatorFeeCoflCoins} listed CoflCoins, rounded down) before creator-side tax or withholding. Customer tax, payment costs and Coflnet-funded promotions do not reduce it.";
    }

    public string Describe(ExpertConfigQuote quote, int listedCoflCoins) =>
        $"You pay {quote.CoinAmount:0.##} CoflCoins, recorded as EUR {quote.GrossEurCents / 100m:0.00} "
        + $"including EUR {quote.VatEurCents / 100m:0.00} transaction tax ({quote.TaxCountry}, "
        + $"{quote.VatRateBasisPoints / 100m:0.##}%). The creator fee is EUR "
        + $"{CreatorFeeEurCents(listedCoflCoins) / 100m:0.00} before creator-side tax or withholding, using the fixed EUR 0.70 per {CreatorFeeCoflCoins} listed CoflCoins schedule (rounded down); any lower charged amount is a Coflnet-funded promotion.";

    public async Task EnsureReady()
    {
        var (uri, token) = Connection();
        using var request = Request(
            HttpMethod.Get, new Uri(uri, "/api/rewards/ready"), token);
        using var response = await clients.CreateClient(nameof(RewardLedgerClient))
            .SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CreatorEligibility> GetCreatorEligibility(
        string creatorUserId,
        string minecraftUuid,
        string creatorAgreementHash)
    {
        if (!Guid.TryParse(minecraftUuid, out _))
            return new(false, false);
        var (uri, token) = OnboardingConnection();
        var path = $"/api/creator-onboarding/{Uri.EscapeDataString(creatorUserId)}/eligibility"
            + $"?minecraftUuid={Uri.EscapeDataString(minecraftUuid ?? "")}"
            + $"&agreementHash={Uri.EscapeDataString(creatorAgreementHash ?? "")}";
        using var request = Request(HttpMethod.Get, new Uri(uri, path), token);
        using var response = await clients.CreateClient(nameof(RewardLedgerClient))
            .SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreatorEligibility>()
            ?? throw new InvalidOperationException(
                "Creator onboarding returned an empty response.");
    }

    public Task<Guid> RecordPending(
        long transactionId,
        string creatorUserId,
        string creatorAgreementHash,
        string buyerUserId,
        string configName,
        int configVersion,
        int listedCoflCoins,
        ExpertConfigQuote quote) => Append(
            $"expert-config:{transactionId}:pending",
            creatorUserId,
            1,
            4,
            CreatorFeeEurCents(listedCoflCoins),
            null,
            creatorAgreementHash,
            "Fixed Expert Content creator fee",
            JsonSerializer.Serialize(new
            {
                transactionId,
                buyerUserId,
                configName,
                configVersion,
                chargedCoflCoins = quote.CoinAmount,
                listedCoflCoins,
                creatorFeeEurCents = CreatorFeeEurCents(listedCoflCoins),
                creatorFeeRule = "300-listed-coflcoins-per-eur"
            }));

    public Task<Guid> RecordAvailable(
        long transactionId,
        string creatorUserId,
        long creatorFeeEurCents,
        Guid pendingId) => Append(
            $"expert-config:{transactionId}:available",
            creatorUserId,
            2,
            4,
            creatorFeeEurCents,
            pendingId,
            null,
            "Expert Config supplied after final CoflCoin payment",
            null);

    private static long Convert(
        decimal coflCoins,
        int eurCents,
        int valuationCoins)
    {
        if (coflCoins < 0 || valuationCoins <= 0 || eurCents <= 0)
            throw new InvalidOperationException(
                "Expert Config EUR valuation is not configured.");
        return (long)Math.Round(
            coflCoins * eurCents / (decimal)valuationCoins,
            MidpointRounding.AwayFromZero);
    }

    private async Task<Guid> Append(
        string reference,
        string rewardAccountId,
        int kind,
        int source,
        long amount,
        Guid? relatedEntryId,
        string offerVersion,
        string reason,
        string detailsJson)
    {
        var (uri, token) = Connection();
        using var request = Request(
            HttpMethod.Post, new Uri(uri, "/api/rewards/entries"), token);
        request.Content = JsonContent.Create(new
        {
            reference,
            rewardAccountId,
            kind,
            source,
            remunerationEurCents = amount,
                relatedEntryId,
                offerVersion,
                reason,
            detailsJson
        });
        using var response = await clients.CreateClient(nameof(RewardLedgerClient))
            .SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RewardEntryResult>()
            ?? throw new InvalidOperationException(
                "The reward ledger returned an empty response.")).Entry.Id;
    }

    private (Uri Uri, string Token) Connection()
    {
        var token = configuration["REWARDS:WRITE_TOKEN"];
        if (!Uri.TryCreate(configuration["REFERRAL_BASE_URL"], UriKind.Absolute, out var uri)
            || token?.Length < 32)
            throw new InvalidOperationException(
                "The Expert Config reward ledger is not configured.");
        return (uri, token);
    }

    private (Uri Uri, string Token) OnboardingConnection()
    {
        var token = configuration["CREATOR_ONBOARDING:READ_TOKEN"];
        if (!Uri.TryCreate(configuration["REFERRAL_BASE_URL"], UriKind.Absolute, out var uri)
            || token?.Length < 32)
            throw new InvalidOperationException(
                "Creator onboarding eligibility is not configured.");
        return (uri, token);
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record RewardEntryResult(RewardEntry Entry);
    private sealed record RewardEntry(Guid Id);
}

public record CreatorEligibility(
    bool Eligible,
    bool PaidPublicationReady);
