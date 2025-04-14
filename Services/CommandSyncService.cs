using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Commands.MC;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Services;

public class CommandSyncService
{
    private ConnectionMultiplexer redis;
    private ILogger<CommandSyncService> logger;

    public CommandSyncService(IConfiguration config, ILogger<CommandSyncService> logger)
    {
        this.logger = logger;
        redis = ConnectionMultiplexer.Connect(config["REDIS_HOST"]);
    }

    /// <summary>
    /// Subscribes to a channel and calls the onMessage function when a message is received.
    /// The function should return true to keep the subscription alive, or false to unsubscribe.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="onMessage"></param>
    /// <returns></returns>
    /// <exception cref="CoflnetException"></exception>
    public async Task<ChannelMessageQueue> Subscribe(SessionInfo info, Func<string, bool> onMessage)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var sub = await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(info.McName.ToLower()));
                sub.OnMessage((value) =>
                {
                    var deserialized = JsonConvert.DeserializeObject<ExecuteRequest>(value.Message);
                    logger.LogInformation($"Received command {deserialized.Command} from {deserialized.MinecraftName} at {deserialized.UserId}");
                    if (!onMessage(deserialized.Command))
                        sub.Unsubscribe();
                });
                return sub;
            }
            catch (Exception e)
            {
                if (i >= 2)
                    throw;
                await Task.Delay(300).ConfigureAwait(false);
            }
        }
        throw new CoflnetException("connection_failed", "connection to command sync failed");
    }
    public async Task Publish(ExecuteRequest request)
    {
        await redis.GetSubscriber().PublishAsync(request.MinecraftName.ToLower(), JsonConvert.SerializeObject(request));
    }

    public class ExecuteRequest
    {
        public string UserId { get; set; }
        public Guid MinecraftUuid { get; set; }
        public string MinecraftName { get; set; }
        public string Command { get; set; }
    }
}

