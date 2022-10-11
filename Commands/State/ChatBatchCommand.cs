using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Confluent.Kafka;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Upload a batch of chat
    /// </summary>
    public class ChatBatchCommand : McCommand
    {
        IProducer<string, UpdateMessage> producer;
        private object Lock = new object();
        private void CreateProducer(MinecraftSocket socket)
        {
            lock (Lock)
            {
                if (producer != null)
                    return;
                var config = socket.GetService<IConfiguration>();
                var producerConfig = new ProducerConfig
                {
                    BootstrapServers = config["KAFKA_HOST"],
                    LingerMs = 100
                };
                producer = new ProducerBuilder<string, UpdateMessage>(producerConfig).SetValueSerializer(SerializerFactory.GetSerializer<UpdateMessage>()).SetDefaultPartitioner((topic, pcount, key, isNull) =>
                {
                    if (isNull)
                        return Random.Shared.Next() % pcount;
                    return new Partition((key[0] << 8 + key[1]) % pcount);
                }).Build();
            }
        }
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            CreateProducer(socket);
            var batch = JsonConvert.DeserializeObject<List<string>>(arguments);
            if (batch[0] == "You cannot view this auction!")
                socket.SendMessage(COFLNET + "You have to use a booster cookie or be on the hub island to open auctions. \nClick to warp to hub", "/hub", "warp to hup");
            if (batch[0].Contains("§a❈ Defense"))
                return Task.CompletedTask; // dismiss stat update
            //socket.SendCommand("debug", "messages received " + JsonConvert.SerializeObject(batch));
            var config = socket.GetService<IConfiguration>();
            var playerId = socket.SessionInfo?.McName;
            if (playerId == "Ekwav")
                Console.WriteLine("produced chat batch " + batch[0]);
            try
            {
                producer.Produce(config["TOPICS:STATE_UPDATE"], new()
                {
                    Key = string.IsNullOrEmpty(playerId) ? null : playerId.Substring(0, 4) + batch[0].Truncate(10),
                    Value = new()
                    {
                        ChatBatch = batch,
                        ReceivedAt = DateTime.UtcNow,
                        PlayerId = playerId,
                        Kind = UpdateMessage.UpdateKind.CHAT,
                        SessionId = socket.SessionInfo.SessionId
                    }
                });
            }
            catch (System.Exception e)
            {
                Console.WriteLine("chat produce failed " + e);
            }
            return Task.CompletedTask;
        }
    }

    [MessagePackObject]
    public class UpdateMessage
    {
        [Key(0)]
        public UpdateKind Kind;

        [Key(1)]
        public DateTime ReceivedAt;
        [Key(3)]
        public List<string> ChatBatch;
        [Key(4)]
        public string PlayerId;
        [Key(5)]
        public string SessionId { get; set; }

        public enum UpdateKind
        {
            UNKOWN,
            CHAT,
            INVENTORY,
            API = 4
        }
    }
}