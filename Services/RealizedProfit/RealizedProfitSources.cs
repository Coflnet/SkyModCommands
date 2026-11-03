using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.FlipTracker.Client.Api;
using Coflnet.Sky.FlipTracker.Client.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// Reads flip tracking, the bazaar profit api and the player state task ledger.
/// The task ledger is called with a plain http client, the generated client does not know the endpoint yet.
/// </summary>
public class RealizedProfitSources : IRealizedProfitSources
{
    public const string HttpClientName = "RealizedProfitTasks";
    private const int BazaarFlipLimit = 1500;
    public const int MaxAccountsPerUser = 20;
    public const int MaxConcurrentCalls = 4;
    private readonly ITrackerApi trackerApi;
    private readonly IBazaarProfitApi bazaarApi;
    private readonly IHttpClientFactory httpFactory;
    private readonly string playerStateBaseUrl;
    private readonly ILogger<RealizedProfitSources> logger;

    public RealizedProfitSources(ITrackerApi trackerApi, IBazaarProfitApi bazaarApi,
        IHttpClientFactory httpFactory, IConfiguration config, ILogger<RealizedProfitSources> logger = null)
    {
        this.logger = logger;
        this.trackerApi = trackerApi;
        this.bazaarApi = bazaarApi;
        this.httpFactory = httpFactory;
        playerStateBaseUrl = (config["PLAYERSTATE_BASE_URL"] ?? "").TrimEnd('/');
    }

    public async Task<long> GetAhProfit(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token)
    {
        var perAccount = await ForEachAccount(uuids,
            (id, _) => trackerApi.GetFlipsOfPlayerAsync(id, from, to, false, 0, token));
        return SumAhProfit(perAccount.SelectMany(f => f));
    }

    /// <summary>
    /// Sums the profit of sold flips, one purchase counts once even when several linked accounts report it
    /// (same dedupe as the flip tracking service). Unsold flips have no realized profit and are skipped.
    /// </summary>
    public static long SumAhProfit(IEnumerable<PastFlip> flips)
    {
        return flips.Where(f => f != null && f.Profit != 0)
            .GroupBy(f => f.PurchaseAuctionId)
            .Select(g => g.Last())
            .Sum(f => (long)f.Profit);
    }

    public async Task<long> GetBazaarProfit(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token)
    {
        var summaries = await ForEachAccount(uuids,
            (id, _) => bazaarApi.BazaarProfitSummaryPlayerUuidGetAsync(id, from, to, BazaarFlipLimit, 0, token));
        return (long)summaries.Sum(s => s.TotalProfit);
    }

    public async Task<long> GetTaskValue(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token)
    {
        var perAccount = await ForEachAccount(uuids, (id, raw) => GetTaskValue(raw, from, to, token));
        return perAccount.Sum();
    }

    /// <summary>
    /// Runs the call for the first <see cref="MaxAccountsPerUser"/> well formed uuids with a limited
    /// number of calls in flight, alt heavy accounts must not fan out hundreds of requests.
    /// </summary>
    private async Task<List<T>> ForEachAccount<T>(IReadOnlyList<string> uuids, Func<Guid, string, Task<T>> call)
    {
        var valid = uuids.Where(u => Guid.TryParse(u, out _)).ToList();
        var capped = valid.Take(MaxAccountsPerUser).ToList();
        if (valid.Count > capped.Count)
            logger?.LogInformation("Capped {count} accounts of one user to {max} for realized profit", valid.Count, MaxAccountsPerUser);
        using var limit = new SemaphoreSlim(MaxConcurrentCalls);
        var results = await Task.WhenAll(capped.Select(async u =>
        {
            await limit.WaitAsync();
            try
            {
                return await call(Guid.Parse(u), u);
            }
            finally
            {
                limit.Release();
            }
        }));
        return results.ToList();
    }

    private async Task<long> GetTaskValue(string uuid, DateTime from, DateTime to, CancellationToken token)
    {
        var client = httpFactory.CreateClient(HttpClientName);
        var url = $"{playerStateBaseUrl}/Task/{Uri.EscapeDataString(uuid)}/realized?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        var json = await client.GetStringAsync(url, token);
        return JsonConvert.DeserializeObject<TaskRealizedResponse>(json)?.Total ?? 0;
    }

    private class TaskRealizedResponse
    {
        [JsonProperty("total")]
        public long Total { get; set; }
    }
}
