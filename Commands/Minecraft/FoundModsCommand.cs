using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class FoundModsCommand : McCommand
{
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        var mods = JsonConvert.DeserializeObject<Response>(arguments);
        return Task.CompletedTask;
    }

    public class Response
    {
        public string[] FileNames { get; set; }
        public string[] FileHashes { get; set; }
        public string[] ModNames { get; set; }
    }
}