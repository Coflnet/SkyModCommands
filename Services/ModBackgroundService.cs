using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.FlipTracker.Client.Api;
using fNbt.Tags;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebSocketSharp;

namespace Coflnet.Sky.ModCommands.Services
{

    public class ModBackgroundService : BackgroundService
    {
        private IServiceScopeFactory scopeFactory;
        private IConfiguration config;
        private ILogger<ModBackgroundService> logger;
        private FlipperService flipperService;
        private CounterService counterService;
        IDelayExemptList delayExemptList;

        private static Prometheus.Counter fastTrackSnipes = Prometheus.Metrics.CreateCounter("sky_fast_snipes", "Count of received fast track redis snipes");

        public ModBackgroundService(
            IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ModBackgroundService> logger, FlipperService flipperService, CounterService counterService, IDelayExemptList iDelayExemptList)
        {
            this.scopeFactory = scopeFactory;
            this.config = config;
            this.logger = logger;
            this.flipperService = flipperService;
            this.counterService = counterService;
            delayExemptList = iDelayExemptList;
        }
        /// <summary>
        /// Called by asp.net on startup
        /// </summary>
        /// <param name="stoppingToken">is canceled when the applications stops</param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            await SubscribeToRedisSnipes(stoppingToken);
            logger.LogInformation("set up fast track flipper");
            await counterService.GetTable().CreateIfNotExistsAsync();
            await LoadDelayExcemptKeys();
            await Task.Delay(2000);
            var client = new WebSocket("ws://localhost:8008/modsocket?SId=123123123123123123&player=test&version=1.5.6-Alpha");
            client.OnOpen += (s, e) =>
            {
                logger.LogInformation("established test connection");
            };
            client.OnError += (s, e) =>
            {
                logger.LogInformation("Could not establish test connection");
            };
            client.Connect();
            await Task.Delay(3000);
            client.Close();
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            logger.LogError("Fast track was stopped");
        }

        private async Task LoadDelayExcemptKeys()
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var trackerApi = scope.ServiceProvider.GetRequiredService<ITrackerApi>();
                var keys = await trackerApi.FlipsExemptGetAsync();
                this.delayExemptList.Exemptions = new(keys.Select(k => (k.ItemTag, k.Key)).ToHashSet());
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not load delay exempt keys");
            }
        }

        public async Task SubscribeToRedisSnipes(CancellationToken stoppingToken)
        {
            var instances = await GetConnections();
            foreach (var multiplexer in instances)
            {
                try
                {
                    SubscribeConnection(multiplexer, stoppingToken);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "redis error");
                }
            }
        }

        private async Task<List<ConnectionMultiplexer>> GetConnections()
        {
            List<ConnectionMultiplexer> instances = new();
            var i = 2;
            while (instances.Count == 0)
            {
                try
                {
                    var instanceStrings = config.GetSection("REDIS_FLIP_INSTANCES").Get<string[]>();
                    if (instanceStrings == null)
                    {
                        logger.LogError("No REDIS_FLIP_INSTANCES found in config, not connecting to any bfcs instances");
                        return instances;
                    }
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

        private void AddOption(List<ConnectionMultiplexer> instances, string item)
        {
            var option = ConfigurationOptions.Parse(item);
            try
            {
                instances.Add(ConnectionMultiplexer.Connect(option));
            }
            catch (System.Exception e)
            {
                var replacedPassword = item.Replace(option.Password, "********");
                logger.LogError(e, "Could not connect to redis: " + replacedPassword);
            }
        }

        private void SubscribeConnection(ConnectionMultiplexer multiplexer, CancellationToken stoppingToken)
        {
            var hostName = System.Net.Dns.GetHostName();
            multiplexer.GetSubscriber().Subscribe("snipes", (chan, val) =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var flip = MessagePackSerializer.Deserialize<LowPricedAuction>(val);
                        if (flip.AdditionalProps.GetValueOrDefault("server") == hostName)
                            return; // already processed
                        if (flip.Finder == LowPricedAuction.FinderType.TFM)
                            FixTfmMetadata(flip);
                        if (flip.TargetPrice < flip.Auction.StartingBid + 100_000)
                            return; // not actually flipable abort
                        if (flip.Auction.Context.ContainsKey("cname"))
                            flip.Auction.Context["cname"] += McColorCodes.DARK_GRAY + "!";
                        flip.AdditionalProps?.TryAdd("bfcs", "redis");
                        await DistributeFlipOnServer(flip).ConfigureAwait(false);
                        if (flip.TargetPrice - flip.Auction.StartingBid > 2000000)
                            logger.LogInformation($"scheduled bfcs {flip.Auction.Uuid} {DateTime.UtcNow.Second}.{DateTime.UtcNow.Millisecond}");
                        fastTrackSnipes.Inc();
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "bfcs error");
                    }
                }, new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token).ConfigureAwait(false);
            });
            logger.LogInformation("Subscribed to " + multiplexer.IsConnected + multiplexer.GetEndPoints().Select(e =>
            {
                var server = multiplexer.GetServer(e);
                return $" {server.EndPoint.AddressFamily}-" + e.ToString();
            }).First());
            multiplexer.GetSubscriber().Subscribe("beat", (chan, val) =>
            {
                if (val == System.Net.Dns.GetHostName())
                    logger.LogInformation("redis heart beat " + val);
            });
            Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    multiplexer.GetSubscriber().Publish("beat", System.Net.Dns.GetHostName());
                }
                logger.LogWarning($"redis heart beat stopped; Cancellation Requested: {stoppingToken.IsCancellationRequested}");
            });

            Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(150), stoppingToken);
                    logger.LogInformation($"Status of Redis multiplexer: {multiplexer.IsConnected}");
                }
            });
        }

        protected virtual async Task DistributeFlipOnServer(LowPricedAuction flip)
        {
            await flipperService.DeliverLowPricedAuction(flip, AccountTier.PREMIUM_PLUS).ConfigureAwait(false);
        }

        private static void FixTfmMetadata(LowPricedAuction flip)
        {
            // rarange nbt
            var compound = flip.Auction.NbtData.Root().Get<NbtList>("i")
                ?.Get<NbtCompound>(0);
            if (flip.Auction.Context == null)
                flip.Auction.Context = new();
            NBT.FillFromTag(flip.Auction, compound, true);
            var lore = string.Join("\n", NBT.GetLore(compound));
            flip.Auction.Context["lore"] = lore;
            if (flip.AdditionalProps.TryGetValue("lbin", out var lbin))
            {
                Console.WriteLine($"Capping target price from {flip.TargetPrice} to {long.Parse(lbin)}");
                flip.TargetPrice = (long)Math.Min(long.Parse(lbin), flip.TargetPrice);
            }
            else
            {
                flip.AdditionalProps?.TryAdd("capped", "not tfm");
            }
            if (Constants.Vanilla.Contains(flip.Auction.Tag.ToLower()) || flip.Auction.Tag.Contains("MIDAS"))
            {
                flip.TargetPrice = 0;
            }
        }
    }
}