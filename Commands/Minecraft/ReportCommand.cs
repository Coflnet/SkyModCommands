using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using OpenTracing.Util;

namespace Coflnet.Sky.Commands.MC
{
    public class ReportCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            if (string.IsNullOrEmpty(arguments) || arguments.Length < 3)
            {
                socket.SendMessage(COFLNET + "Please add some information to the report, ie. what happened, what do you think should have happened.");
            }
            System.Threading.ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
            using var reportSpan = socket.tracer.BuildSpan("report")
                        .WithTag("message", arguments.Truncate(150))
                        .WithTag("error", "true")
                        .WithTag("mcId", JsonConvert.SerializeObject(socket.SessionInfo.McName))
                        .WithTag("uuid", JsonConvert.SerializeObject(socket.SessionInfo.McUuid))
                        .WithTag("userId", JsonConvert.SerializeObject(socket.sessionLifesycle.AccountInfo?.Value))
                        .WithTag("workerThreads", workerThreads)
                        .WithTag("timestamp", DateTime.UtcNow.ToLongTimeString())
                        .WithTag("completionPortThreads", completionPortThreads)
                        .AsChildOf(socket.ConSpan).StartActive();

            reportSpan.Span.Log(JsonConvert.SerializeObject(socket.Settings));
            reportSpan.Span.Log(JsonConvert.SerializeObject(socket.TopBlocked?.Take(80)));
            reportSpan.Span.Log("session info " + JsonConvert.SerializeObject(socket.SessionInfo));
            var spanId = reportSpan.Span.Context.SpanId.Truncate(6);
            reportSpan.Span.SetTag("id", spanId);
            try
            {
                AddAllSettings(reportSpan);
            }
            catch (Exception e)
            {
                reportSpan.Span.Log(e.Message + "\n" + e.StackTrace);
            }

            dev.Logger.Instance.Error($"Report with id {spanId} {arguments}");
            dev.Logger.Instance.Info(JsonConvert.SerializeObject(socket.TopBlocked?.Take(10)));
            dev.Logger.Instance.Info(JsonConvert.SerializeObject(socket.Settings));

            socket.SendMessage(COFLNET + "Thanks for your report :)\n If you need further help, please refer to this report with " + McColorCodes.AQUA + spanId, "http://" + spanId);
            return Task.CompletedTask;
        }

        private static void AddAllSettings(OpenTracing.IScope reportSpan)
        {
            var otherUsers = FlipperService.Instance.Connections;
            var result = otherUsers.Select(c => new
            {
                c.ChannelCount,
                c.Connection.Settings?.Visibility,
                c.Connection.Settings?.ModSettings,
                c.Connection.Settings?.BasedOnLBin,
                c.Connection.Settings?.AllowedFinders,
                c.Connection.UserId
            });
            reportSpan.Span.Log(JsonConvert.SerializeObject(result, Formatting.Indented));
            System.Console.WriteLine(JsonConvert.SerializeObject(result));
        }
    }
}