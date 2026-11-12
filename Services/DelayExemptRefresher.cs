using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Periodically replaces the delay exempt keys with the current ones from the tracker.
/// Runs off the flip path; readers see either the old or the new set.
/// </summary>
public class DelayExemptRefresher
{
    /// <summary>
    /// Retry interval while no keys are loaded yet
    /// </summary>
    public static readonly TimeSpan EmptyRetryInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(10);

    private readonly Func<CancellationToken, Task<HashSet<(string, string)>>> fetch;
    private readonly IDelayExemptList list;
    private readonly ILogger logger;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public DelayExemptRefresher(
        Func<CancellationToken, Task<HashSet<(string, string)>>> fetch,
        IDelayExemptList list,
        ILogger logger,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        this.fetch = fetch;
        this.list = list;
        this.logger = logger;
        this.delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Loads immediately and then every <paramref name="interval"/> until cancelled
    /// </summary>
    public async Task RunAsync(TimeSpan interval, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await RefreshOnce(token).ConfigureAwait(false);
                var wait = list.Exemptions.Count == 0 ? EmptyRetryInterval : interval;
                await delay(wait, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // service is stopping
        }
    }

    public async Task RefreshOnce(CancellationToken token)
    {
        try
        {
            var keys = await fetch(token).ConfigureAwait(false);
            if (keys.Count == 0)
            {
                logger.LogWarning("Delay exempt keys came back empty, keeping the previous ones");
                return;
            }
            list.Exemptions = keys;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not load delay exempt keys");
        }
    }
}

