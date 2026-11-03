using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>Refreshes the realized profit gauges every few minutes, errors are logged and retried next round.</summary>
public class RealizedProfitMetricsService : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private readonly RealizedProfitMetricsExporter exporter;
    private readonly ILogger<RealizedProfitMetricsService> logger;

    public RealizedProfitMetricsService(RealizedProfitMetricsExporter exporter, ILogger<RealizedProfitMetricsService> logger)
    {
        this.exporter = exporter;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RefreshSafe(stoppingToken);
        }
        while (await WaitNext(timer, stoppingToken));
    }

    private static async Task<bool> WaitNext(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal async Task RefreshSafe(CancellationToken token)
    {
        try
        {
            await exporter.Refresh(token);
        }
        catch (Exception e)
        {
            if (token.IsCancellationRequested)
                return;
            logger.LogError(e, "Could not refresh realized profit metrics");
        }
    }
}
