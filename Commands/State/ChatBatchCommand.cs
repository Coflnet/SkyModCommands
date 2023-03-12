using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using System.Linq;
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

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var batch = JsonConvert.DeserializeObject<List<string>>(arguments);
            if (batch.Count == 0)
                return;
            if (batch[0] == "You cannot view this auction!")
                socket.SendMessage(COFLNET + "You have to use a booster cookie or be on the hub island to open auctions. \nClick to warp to hub", "/hub", "warp to hup");
            if (batch.All(l => l.Contains("§a❈ Defense")))
                return; // dismiss stat update
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
                    if (item.StartsWith("You purchased"))
                        socket.GetService<PreApiService>().PurchaseMessage(socket, item);
                    if (item.StartsWith("BIN Auction started"))
                        await socket.GetService<PreApiService>().ListingMessage(socket, item);
                }
            }
            catch (System.Exception e)
            {
                Console.WriteLine("chat produce failed " + e);
            }
        }
    }
}