using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Confluent.Kafka;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;


namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Upload a batch of chat
    /// </summary>
    public class ChatBatchCommand : McCommand
    {

        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var batch = JsonConvert.DeserializeObject<List<string>>(arguments);
            if (batch[0] == "You cannot view this auction!")
                socket.SendMessage(COFLNET + "You have to use a booster cookie or be on the hub island to open auctions. \nClick to warp to hub", "/hub", "warp to hup");
            if (batch[0].Contains("§a❈ Defense"))
                return Task.CompletedTask; // dismiss stat update
            var config = socket.GetService<IConfiguration>();
            var playerId = socket.SessionInfo?.McName;
            if (playerId == "Ekwav")
                Console.WriteLine("produced chat batch " + batch[0]);
            try
            {
                socket.GetService<IStateUpdateService>().Produce(playerId, new()
                {
                    ChatBatch = batch,
                    ReceivedAt = DateTime.UtcNow,
                    PlayerId = playerId,
                    Kind = UpdateMessage.UpdateKind.CHAT,
                    SessionId = socket.SessionInfo.SessionId
                });

                foreach (var item in batch)
                {
                    if (item.StartsWith("You purchased ◆ Lava Rune I for 1 coins!"))
                        socket.GetService<PreApiService>().PurchaseMessage(item, socket);
                }
            }
            catch (System.Exception e)
            {
                Console.WriteLine("chat produce failed " + e);
            }
            return Task.CompletedTask;
        }
    }
}