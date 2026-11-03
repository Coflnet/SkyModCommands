using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>Consumes disconnect snapshots one at a time, so the apis see a steady trickle instead of bursts.</summary>
public class RealizedProfitBackgroundService : BackgroundService
{
    private readonly RealizedProfitQueue queue;
    private readonly RealizedProfitCollector collector;
    private readonly ILogger<RealizedProfitBackgroundService> logger;

    public RealizedProfitBackgroundService(RealizedProfitQueue queue, RealizedProfitCollector collector,
        ILogger<RealizedProfitBackgroundService> logger)
    {
        this.queue = queue;
        this.collector = collector;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await foreach (var snapshot in queue.Reader.ReadAllAsync(stoppingToken))
            await ProcessSafe(snapshot, stoppingToken);
    }

    private async Task ProcessSafe(RealizedProfitSnapshot snapshot, CancellationToken token)
    {
        try
        {
            await collector.Process(snapshot, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not store realized profit of user {user}", snapshot.UserId);
        }
    }
}
