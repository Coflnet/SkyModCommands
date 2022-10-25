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
using Coflnet.Sky.Core;
using MessagePack;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;

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
                Task.Run(async () =>
                {
                    try
                    {
                        var flip = MessagePackSerializer.Deserialize<LowPricedAuction>(val);
                        if (flip.Auction.Context.ContainsKey("cname"))
                            flip.Auction.Context["cname"] += McColorCodes.DARK_GRAY + "!";
                        flip.AdditionalProps?.TryAdd("bfcs", "redis");
                        await FlipperService.Instance.DeliverLowPricedAuction(flip, AccountTier.PREMIUM_PLUS).ConfigureAwait(false);
                        logger.LogInformation($"sheduled bfcs {flip.Auction.UId} {DateTime.UtcNow.Second}.{DateTime.UtcNow.Millisecond}");
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "bfcs error");
                    }
                }).ConfigureAwait(false);
            });
            logger.LogInformation("set up fast track flipper");
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }

        private ModService GetService()
        {
            return scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ModService>();
        }
    }
}