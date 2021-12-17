using System.Threading.Tasks;
using hypixel;
using Newtonsoft.Json;
using OpenTracing.Util;

namespace Coflnet.Sky.Commands.MC
{
    public class ReportCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            if(string.IsNullOrEmpty(arguments) || arguments.Length < 3)
            {
                socket.SendMessage(COFLNET + "Please add some information to the report, ie. what happened, what do you think should have happened.");
            }
            using var reportSpan = socket.tracer.BuildSpan("report")
                        .WithTag("message", arguments.Truncate(150))
                        .WithTag("error", "true")
                        .WithTag("mcId", JsonConvert.SerializeObject(socket.McId))
                        .AsChildOf(socket.ConSpan).StartActive();
                        
            reportSpan.Span.Log(JsonConvert.SerializeObject(socket.Settings));
            reportSpan.Span.Log(JsonConvert.SerializeObject(socket.TopBlocked));
            var spanId = reportSpan.Span.Context.SpanId.Truncate(6);
            reportSpan.Span.SetTag("id", spanId);

            socket.SendMessage(COFLNET + "Thanks for your report :)\n If you need further help, please refer to this report with " + spanId, spanId);
            return Task.CompletedTask;
        }
    }
}