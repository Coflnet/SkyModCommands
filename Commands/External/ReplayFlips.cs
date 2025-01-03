using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Confluent.Kafka;
using Coflnet.Kafka;
using System;
using System.Threading;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Replay all flips from the last x hours",
    "Meant for config creators to test their config")]
public class ReplayFlips : ArgumentsCommand
{
    protected override string Usage => "[hours={2}]";
    public override bool IsPublic => true;

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        //if (!await socket.ReguirePremPlus())
        //    return;
        if (socket.Settings.BlockExport)
        {
            socket.Dialog(db => db.MsgLine("You seem to have a config loaded you don't own/made. This feature is only available for config creators"));
            return;
        }
        if (!double.TryParse(args["hours"], out var hours))
        {
            SendUsage(socket, "Hours have to be a number");
            return;
        }
        if (hours > 100)
        {
            socket.Dialog(db => db.MsgLine("You can only replay the last 100 hours"));
            return;
        }
        if ((socket is MinecraftSocket s) && s.HasFlippingDisabled())
        {
            socket.Dialog(db => db.MsgLine($"You currently can't receive flips, Check {McColorCodes.AQUA}/cofl blocked{McColorCodes.RESET} for why",
                    "/cofl blocked", $"Click to run {McColorCodes.AQUA}/cofl blocked"));
            return;
        }
        var iConfig = socket.GetService<IConfiguration>();
        var kafkaTopic = iConfig.GetValue<string>("TOPICS:LOW_PRICED");
        var conf = new ConsumerConfig(KafkaCreator.GetClientConfig(iConfig))
        {
            GroupId = "sky-replay-flips",
        };
        using var consumer = new ConsumerBuilder<Ignore, LowPricedAuction>(conf).SetValueDeserializer(Coflnet.Kafka.SerializerFactory.GetDeserializer<LowPricedAuction>()).Build();
        // seek to 10 hours ago
        var partition = new TopicPartition(kafkaTopic, 0);
        var timeStamp = new Timestamp(DateTime.UtcNow.AddHours(-hours), TimestampType.CreateTime);
        var offsets = consumer.OffsetsForTimes([new TopicPartitionTimestamp(partition, timeStamp)], TimeSpan.FromSeconds(10));

        consumer.Assign(offsets);
        socket.Dialog(db => db.MsgLine($"Replaying flips of the last {hours} hours..."));
        var count = 0;
        while (true)
        {
            try
            {
                var cr = consumer.Consume(new CancellationTokenSource(1000).Token);
                if (cr == null) break;
                await socket.SendFlip(cr.Message.Value);
                count++;
                if (count % 50 == 0)
                    socket.sessionLifesycle.spamController.Reset();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                throw;
            }
        }
        socket.Dialog(db => db.MsgLine($"Replaying {McColorCodes.AQUA}{count}{McColorCodes.RESET} auctions finished, \n{McColorCodes.GRAY}wana replay active auctions against user finder? Try /cofl replayactive"));
    }
}