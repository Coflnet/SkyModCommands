using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class AhOpenCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var uuid = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(arguments);
            var name = await socket.GetPlayerName(uuid);
            if(name == null)
            {
                socket.SendMessage(new DialogBuilder().Msg("Could not retrieve the sellers name to open ah"));
                return;
            }
            using var span = socket.tracer.BuildSpan("ahopen").AsChildOf(socket.ConSpan.Context).StartActive();
            span.Span.SetTag("name",name);
            socket.ExecuteCommand($"/ah {name}");
        }
    }
}