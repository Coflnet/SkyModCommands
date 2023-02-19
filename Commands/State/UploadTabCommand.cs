using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class UploadTabCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = JsonConvert.DeserializeObject<string[]>(arguments);
            // does nothing for now
            return Task.CompletedTask;
        }
    }
}