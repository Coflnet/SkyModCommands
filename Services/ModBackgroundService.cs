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
using System.Collections.Generic;

namespace Coflnet.Sky.ModCommands.Services
{

    public class ModBackgroundService : BackgroundService
    {
        private IServiceScopeFactory scopeFactory;
        private IConfiguration config;
        private ILogger<ModBackgroundService> logger;

        public ModBackgroundService(
            IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ModBackgroundService> logger)
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
            var instances = await GetConnections();
            foreach (var multiplexer in instances)
            {
                SubscribeConnection(multiplexer);
            }
            logger.LogInformation("set up fast track flipper");
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }

        private async Task<List<ConnectionMultiplexer>> GetConnections()
        {
            List<ConnectionMultiplexer> instances = new();
            var i = 2;
            while (instances.Count == 0)
            {
                try
                {
                    var oldOption = config["FLIP_REDIS_OPTIONS"];
                    if (oldOption != null)
                    {
                        logger.LogInformation("using legacy flip sniper option");
                        AddOption(instances, oldOption);
                        return instances;
                    }
                    var instanceStrings = config.GetSection("REDIS_FLIP_INSTANCES").Get<string[]>();
                    foreach (var item in instanceStrings)
                    {
                        AddOption(instances, item);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "connecting to flip redis");
                    await Task.Delay(i++ * 10000);
                }
            }

            return instances;
        }

        private static void AddOption(List<ConnectionMultiplexer> instances, string item)
        {
            var option = ConfigurationOptions.Parse(item);
            instances.Add(ConnectionMultiplexer.Connect(option));
        }

        private void SubscribeConnection(ConnectionMultiplexer multiplexer)
        {
            multiplexer.GetSubscriber().Subscribe("snipes", (chan, val) =>
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
        }

        private ModService GetService()
        {
            return scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ModService>();
        }
    }
}