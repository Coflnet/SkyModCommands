using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class FoundModsCommand : McCommand
{
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        var mods = JsonConvert.DeserializeObject<Response>(arguments);
        var current = socket.AccountInfo.CaptchaType;
        if (current != "optifine" && current != "vertical" && mods.FileNames.Any(n => n.ToLower().Contains("optifine")))
        {
            socket.AccountInfo.CaptchaType = "optifine";
            socket.Dialog(db => db.Msg("Changed captcha type because you use optifine"));
        }
        return Task.CompletedTask;
    }

    public class Response
    {
        public string[] FileNames { get; set; }
        public string[] FileHashes { get; set; }
        public string[] ModNames { get; set; }
    }
}