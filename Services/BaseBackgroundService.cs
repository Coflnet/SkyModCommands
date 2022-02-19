using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using System;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.ModCommands.Controllers;
using StackExchange.Redis;
using hypixel;
using MessagePack;

namespace Coflnet.Sky.ModCommands.Services
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
            await Task.Yield();
            ConnectionMultiplexer redis = null;
            var i = 2;
            while (redis == null)
            {
                try
                {

                    var redisOptions = ConfigurationOptions.Parse(config["FLIP_REDIS_OPTIONS"]);
                    redis = ConnectionMultiplexer.Connect(redisOptions);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "connecting to flip redis");
                    await Task.Delay(i++ * 10000);
                }
            }
            redis.GetSubscriber().Subscribe("snipes", (chan, val) =>
            {
                var flip = MessagePackSerializer.Deserialize<LowPricedAuction>(val);
                flip.Auction.ItemName += "!";
                FlipperService.Instance.DeliverLowPricedAuction(flip);
                logger.LogInformation($"sheduled bfcs {flip.Auction.UId} {DateTime.Now.Second}.{DateTime.Now.Millisecond}");
                
            });
            logger.LogInformation("set up fast track flipper");
            return;
        }

        private ModService GetService()
        {
            return scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ModService>();
        }
    }
}