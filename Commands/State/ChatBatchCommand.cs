using System.Threading.Tasks;
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
            var batch = JsonConvert.DeserializeObject<string[]>(arguments);
            if (batch[0] == "You cannot view this auction!")
                socket.SendMessage(COFLNET + "You have to use a booster cookie or be on the hub island to open auctions. \nClick to warp to hub", "/hub", "warp to hup");
            //socket.SendCommand("debug", "messages received " + JsonConvert.SerializeObject(batch));
            // does nothing for now
            return Task.CompletedTask;
        }
    }
}