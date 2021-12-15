using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Base.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using System;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Base.Controllers;

namespace Coflnet.Sky.Base.Services
{

    public class BaseBackgroundService : BackgroundService
    {
        private IServiceScopeFactory scopeFactory;
        private IConfiguration config;
        private ILogger<BaseBackgroundService> logger;

        public BaseBackgroundService(
            IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<BaseBackgroundService> logger)
        {
            this.scopeFactory = scopeFactory;
            this.config = config;
            this.logger = logger;
        }
        /// <summary>
        /// Called by asp.net on startup
        /// </summary>
        /// <param name="stoppingToken">is canceled when the applications stops</param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<BaseDbContext>();
            // make sure all migrations are applied
            await context.Database.MigrateAsync();

            var flipCons = Coflnet.Kafka.KafkaConsumer.Consume<LowPricedAuction>(config["KAFKA_HOST"], config["TOPICS:LOW_PRICED"], async lp =>
            {
                var service = GetService();
                await service.AddFlip(new Flip()
                {
                    AuctionId = lp.UId,
                    FinderType = lp.Finder,
                    TargetPrice = lp.TargetPrice,
                });
            }, stoppingToken, "flipbase");

            await Task.WhenAll(flipCons);
        }

        private BaseService GetService()
        {
            return scopeFactory.CreateScope().ServiceProvider.GetRequiredService<BaseService>();
        }
    }
}