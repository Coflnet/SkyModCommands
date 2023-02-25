using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class LoadFlipHistory : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!socket.GetService<ModeratorService>().IsModerator(socket))
            throw new CoflnetException("forbidden", "You are not allowed to do this");
        var playerId = JsonConvert.DeserializeObject<string>(arguments);
        socket.SendMessage(COFLNET + $"Started refreshing flips for {playerId}", null, "this might take a while")
        if (playerId.Length < 30)
            playerId = (await socket.GetPlayerUuid(playerId)).Trim('"');

        var config = socket.GetService<IConfiguration>();
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = config["KAFKA_HOST"],
            LingerMs = 100
        };
        using var producer = new ProducerBuilder<string, SaveAuction>(producerConfig).SetValueSerializer(SerializerFactory.GetSerializer<SaveAuction>()).Build();
        var count = 0;
        using (var context = new HypixelContext())
        {
            var maxTime = new DateTime(2023, 1, 10);
            var numericId = await context.Players.Where(p => p.UuId == playerId).Select(p => p.Id).FirstAsync();
            Console.WriteLine($"Loading flips for {playerId} ({numericId})");
            var auctions = context.Auctions
                .Where(a => a.SellerId == numericId && a.End < maxTime)
                .Include(a => a.NbtData)
                .Include(a => a.Enchantments);
            foreach (var auction in auctions)
            {
                if (!auction.FlatenedNBT.ContainsKey("uid"))
                    continue;

                producer.Produce(config["TOPICS:LOAD_FLIPS"], new Message<string, SaveAuction> { Key = auction.Uuid, Value = auction });
                count++;
            }
        }
        producer.Flush(TimeSpan.FromSeconds(10));
        socket.SendMessage(COFLNET + $"Potential {count} flips for {playerId} found, submitted for processing", null, "this might take a few minutes to complete");
    }
}