using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using OpenTracing;
using OpenTracing.Util;

namespace Coflnet.Sky.Commands.MC
{
    public class ReportCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (string.IsNullOrEmpty(arguments) || arguments.Length < 3)
            {
                socket.SendMessage(COFLNET + "Please add some information to the report, ie. what happened, what do you think should have happened.");
            }
            string spanId;
            using var singleReportSpan = socket.tracer.BuildSpan("report").StartActive();
            CreateReport(socket, arguments, socket.ConSpan, out spanId);

            dev.Logger.Instance.Error($"Report with id {spanId} {arguments}");
            dev.Logger.Instance.Info(JsonConvert.SerializeObject(socket.TopBlocked?.Take(10)));
            dev.Logger.Instance.Info(JsonConvert.SerializeObject(socket.Settings));

            socket.SendMessage(COFLNET + "Thanks for your report :)\n If you need further help, please refer to this report with " + McColorCodes.AQUA + spanId, "http://" + singleReportSpan.Span.Context.TraceId, "click to get full link");
            await Task.Delay(5);
            // repost 
            CreateReport(socket, arguments, singleReportSpan.Span, out string generalspanId);
        }

        private static void CreateReport(MinecraftSocket socket, string arguments, ISpan parentSpan,  out string spanId)
        {
            using IScope reportSpan = socket.tracer.BuildSpan("report")
                                    .WithTag("message", arguments.Truncate(150))
                                    .WithTag("error", "true")
                                    .WithTag("mcId", JsonConvert.SerializeObject(socket.SessionInfo.McName))
                                    .WithTag("uuid", JsonConvert.SerializeObject(socket.SessionInfo.McUuid))
                                    .WithTag("userId", JsonConvert.SerializeObject(socket.sessionLifesycle.AccountInfo?.Value))
                                    .WithTag("timestamp", DateTime.UtcNow.ToLongTimeString())
                                    .AsChildOf(parentSpan).StartActive();
            using var settingsSpan = socket.tracer.BuildSpan("settings").AsChildOf(reportSpan.Span.Context).StartActive();
            settingsSpan.Span.Log(JsonConvert.SerializeObject(socket.Settings, Formatting.Indented));
            using var blockedSpan = socket.tracer.BuildSpan("blocked").AsChildOf(reportSpan.Span.Context).StartActive();
            blockedSpan.Span.Log(JsonConvert.SerializeObject(socket.TopBlocked?.Take(80), Formatting.Indented));
            using var lastSentSpan = socket.tracer.BuildSpan("lastSent").AsChildOf(reportSpan.Span.Context).StartActive();
            lastSentSpan.Span.Log(JsonConvert.SerializeObject(socket.LastSent.OrderByDescending(s => s.Auction.Start).Take(20), Formatting.Indented));
            reportSpan.Span.Log("session info " + JsonConvert.SerializeObject(socket.SessionInfo));
            spanId = reportSpan.Span.Context.SpanId.Truncate(6);
            reportSpan.Span.SetTag("id", spanId);
            TryAddingAllSettings(reportSpan);
        }

        public static void TryAddingAllSettings(IScope reportSpan)
        {
            try
            {
                AddAllSettings(reportSpan);
            }
            catch (Exception e)
            {
                reportSpan.Span.Log(e.Message + "\n" + e.StackTrace);
            }
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