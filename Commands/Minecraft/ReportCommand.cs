using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;
using System.Diagnostics;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Reports an issue to the developer",
        "/cofl report <message>",
        "When executed returns you a case id.",
        "Please use that id to post into the bug report channel on discord.",
        "Isses can be fixed very quickly if",
        "you include as much information as possible.")]
    public class ReportCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (string.IsNullOrEmpty(arguments) || arguments.Length < 3)
            {
                socket.SendMessage(COFLNET + "Please add some information to the report, ie. what happened, what do you think should have happened.");
            }
            string spanId;
            using var singleReportSpan = socket.CreateActivity("report");
            CreateReport(socket, arguments, socket.ConSpan, out spanId);

            dev.Logger.Instance.Error($"Report with id {spanId} {arguments} {singleReportSpan?.Context.TraceId}");
            dev.Logger.Instance.Info(JsonConvert.SerializeObject(socket.TopBlocked?.Take(10)));
            dev.Logger.Instance.Info(JsonConvert.SerializeObject(socket.Settings));

            socket.SendMessage(COFLNET + "Thanks for your report :)\n If you need further help, please refer to this report with " + McColorCodes.AQUA + spanId, "http://" + singleReportSpan.Context.TraceId, "click to get full link");
            socket.Dialog(db => db.MsgLine("If not done already consider creating a new thread by writing into our bug-report channel on discord. [click to open]", "https://discord.com/channels/267680588666896385/884002032392998942", "redirects to discord"));
            await Task.Delay(2000).ConfigureAwait(false);
            // repost 
            CreateReport(socket, arguments, singleReportSpan, out string generalspanId);
            var inventory = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdLastChestGetAsync(socket.SessionInfo.McName);
            using var x = socket.CreateActivity("inventory", singleReportSpan).Log(JsonConvert.SerializeObject(inventory));
        }

        private static void CreateReport(MinecraftSocket socket, string arguments, Activity parentSpan, out string spanId)
        {
            using var reportSpan = socket.CreateActivity("report", parentSpan)
                                    .AddTag("message", arguments.Truncate(150))
                                    .AddTag("error", "true")
                                    .AddTag("mcId", socket.SessionInfo.McName)
                                    .AddTag("uuid", socket.SessionInfo.McUuid)
                                    .AddTag("userId", JsonConvert.SerializeObject(socket.sessionLifesycle.AccountInfo?.Value))
                                    .AddTag("timestamp", DateTime.UtcNow.ToLongTimeString())
                                    .AddTag("instance", System.Net.Dns.GetHostName())
                                    .AddTag("clientIp", socket.ClientIp);
            spanId = reportSpan?.Context.TraceId.ToString().Truncate(6);
            reportSpan.SetTag("id", spanId);
            using (var settingsSpan = socket.CreateActivity("settings", reportSpan))
                settingsSpan.Log(JsonConvert.SerializeObject(new
                {
                    socket.Settings?.Visibility,
                    socket.Settings?.ModSettings,
                    socket.Settings?.BasedOnLBin,
                    socket.Settings?.AllowedFinders,
                    socket.Settings?.MaxCost,
                    socket.Settings?.MinProfit,
                    socket.Settings?.MinProfitPercent,
                    socket.Settings?.MinVolume,
                    socket.Settings?.WhitelistAfterMain,
                    Blacklist = socket.Settings?.BlackList?.Select(b => new { b.filter, b.ItemTag }),
                    Whitelist = socket.Settings?.WhiteList?.Select(b => new { b.filter, b.ItemTag })

                }, Formatting.Indented));
            using (var blockedSpan = socket.CreateActivity("blocked", reportSpan))
                for (int i = 0; i < 5; i++)
                    blockedSpan.Log(JsonConvert.SerializeObject(socket.TopBlocked?.OrderByDescending(b => b.Now).Select(b => $"{b.Flip.Auction.Uuid} {b.Now} {b.Reason}\n").Skip(i * 25).Take(25), Formatting.Indented));
            using (var lastSentSpan = socket.CreateActivity("lastSent", reportSpan))
            {
                var toBeLogged = socket.LastSent.OrderByDescending(s => s.Auction.Start).Take(20).Select(f => {
                    f.Auction.CoopMembers?.Clear();
                    f.Auction.NbtData = null;
                    f.Auction.Enchantments = null;

                    return f;
                });
                lastSentSpan.Log(JsonConvert.SerializeObject(toBeLogged, Formatting.Indented), 30_000);
            }
            reportSpan.Log("delay: " + socket.sessionLifesycle.CurrentDelay + "\nsession info " + JsonConvert.SerializeObject(socket.SessionInfo, Formatting.Indented), 30_000);
            TryAddingAllSettings(socket, reportSpan);
            socket.Send(Response.Create("getMods", 0));
            reportSpan.Dispose();
        }

        public static void TryAddingAllSettings(MinecraftSocket socket, Activity reportSpan)
        {
            try
            {
                AddAllSettings(socket, reportSpan);
            }
            catch (Exception e)
            {
                reportSpan.Log(e.Message + "\n" + e.StackTrace);
            }
        }

        private static void AddAllSettings(MinecraftSocket socket, Activity reportSpan)
        {
            var otherUsers = socket.GetService<FlipperService>().Connections;
            var result = otherUsers.Select(c => new
            {
                c.ChannelCount,
                c.Connection.Settings?.Visibility,
                c.Connection.Settings?.ModSettings,
                c.Connection.Settings?.BasedOnLBin,
                c.Connection.Settings?.AllowedFinders,
                c.Connection.UserId
            });
            reportSpan.Log(JsonConvert.SerializeObject(result, Formatting.Indented));
        }
    }
}