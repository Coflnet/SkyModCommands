using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

public class MuseumDonationCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MuseumDonationCleanupService> _logger;
    
    // Clean up every 10 minutes since donations are only stored for 20 minutes
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

    public MuseumDonationCleanupService(IServiceScopeFactory scopeFactory, ILogger<MuseumDonationCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Museum donation cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var museumDonationService = scope.ServiceProvider.GetRequiredService<IMuseumDonationService>();
                
                museumDonationService.ClearOldDonations();
                
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during museum donation cleanup");
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); // Retry in 10 minutes on error
            }
        }

        _logger.LogInformation("Museum donation cleanup service stopped");
    }
}
