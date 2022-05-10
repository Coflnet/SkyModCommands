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
            socket.SendCommand("debug", "messages received " + JsonConvert.SerializeObject(batch));
            // does nothing for now
            return Task.CompletedTask;
        }
    }
}