using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class SoundCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var name = JsonConvert.DeserializeObject<string>(arguments);
            socket.SendSound(name);
            socket.SendMessage("playing " + name);
            return Task.CompletedTask;
        }
    }
}