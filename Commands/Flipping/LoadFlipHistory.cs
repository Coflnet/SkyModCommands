using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Kafka;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.MC;
public class LoadFlipHistory : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!socket.SessionInfo.VerifiedMc)
            throw new CoflnetException("not_verified", "You need to verify your minecraft account before executing this command.");
        var args = JsonConvert.DeserializeObject<string>(arguments);
        var playerId = args.Split(' ')[0];
        var allowedAccounts = await socket.sessionLifesycle.GetMinecraftAccountUuids();
        if (playerId.Length > 3 && playerId.Length < 30)
            playerId = (await socket.GetPlayerUuid(playerId)).Trim('"');
        if (int.TryParse(args.Split(' ').Last(), out var days) && !args.Contains(' ') || args.Length < 3)
        {
            playerId = socket.SessionInfo.McUuid;
        }
        else if (allowedAccounts.Contains(playerId))
        {
            // nothing more todo
        }
        else if (!socket.GetService<ModeratorService>().IsModerator(socket))
            throw new CoflnetException("forbidden", "You are not allowed to do update accounts you didn't verify");
        if (days == 0)
            days = 7;
        var redis = socket.GetService<ConnectionMultiplexer>();
        if ((await redis.GetDatabase().StringGetAsync("flipreload" + playerId)).HasValue)
        {
            socket.Dialog(db => db.MsgLine("Flips have already being reloaded recently, this can take multiple hours. \nLots of number crunching :)"));
            return;
        }
        await redis.GetDatabase().StringSetAsync("flipreload" + playerId, "true", TimeSpan.FromMinutes(10));
        socket.SendMessage(COFLNET + $"Started refreshing flips of {playerId} for {days} days", null, "this might take a while");

        var config = socket.GetService<IConfiguration>();
        var creator = socket.GetService<Kafka.KafkaCreator>();
        await creator.CreateTopicIfNotExist(config["TOPICS:LOAD_FLIPS"], 2);
        using var producer = creator.BuildProducer<string, SaveAuction>();
        var count = 0;
        var maxTime = DateTime.UtcNow; new DateTime(2023, 1, 10);
        var minTime = maxTime.AddDays(-1);
        for (int i = 0; i < days; i++)
            using (var context = new HypixelContext())
            {
                var numericId = await context.Players.Where(p => p.UuId == playerId).Select(p => p.Id).FirstAsync();
                Console.WriteLine($"Loading flips for {playerId} ({numericId})");
                var auctions = context.Auctions
                    .Where(a => a.SellerId == numericId && a.End < maxTime && a.End > minTime && a.HighestBidAmount > 0)
                    .Include(a => a.NbtData)
                    .Include(a => a.Enchantments);
                foreach (var auction in auctions)
                {
                    producer.Produce(config["TOPICS:LOAD_FLIPS"], new Message<string, SaveAuction> { Key = auction.Uuid, Value = auction });
                    count++;
                }
                maxTime = minTime;
                minTime = maxTime.AddDays(-1);
            }
        var result = producer.Flush(TimeSpan.FromSeconds(10));
        socket.SendMessage(COFLNET + $"Potential {count} flips for {playerId} found, submitted for processing", null,
            $"this might take a few minutes to complete\n{McColorCodes.GRAY}Flush Result: {result}");
    }
}