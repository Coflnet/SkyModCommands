using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class AhOpenCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var uuid = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(arguments);
            var name = await socket.GetPlayerName(uuid);
            socket.ExecuteCommand($"/ah {name}");
        }
    }
}