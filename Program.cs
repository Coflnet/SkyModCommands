using Coflnet.Core;
using Coflnet.Security.OpenBao;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebSocketSharp.Server;
using Coflnet.Sky.Commands.MC;
using System.Threading.Tasks;
using System;
using Coflnet.Sky.ModCommands.Services.Vps;
using Coflnet.Sky.Commands.Shared;
using System.Text;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.ModCommands.MC
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Starting ModCommands " + System.Net.Dns.GetHostName());
            if (!int.TryParse(args.Length > 0 ? args[0] : "", out int port))
                port = 8008;
            var server = new HttpServer(port);
            ConfigureSessionCleanup(server);
            server.AddWebSocketService<MinecraftSocket>("/modsocket");
            server.AddWebSocketService<VpsSocket>("/instances");
            server.Log.Level = WebSocketSharp.LogLevel.Debug;
            server.Log.Output = (data, s) => Console.WriteLine(data);
            server.OnGet += async (s, e) =>
            {
                try
                {
                    if (e.Request.Url.AbsolutePath.StartsWith("/instances/log"))
                    {
                        await HandleLogRequest(e);
                        return;
                    }
                }
                catch (CoflnetException ex)
                {
                    e.Response.StatusCode = 400;
                    e.Response.ContentType = "text/plain";
                    e.Response.ContentEncoding = Encoding.UTF8;
                    var response = Encoding.UTF8.GetBytes(ex.Message);
                    e.Response.ContentLength64 = response.Length;
                    e.Response.Close(response, true);
                    return;
                }
                e.Response.StatusCode = 201;
            };
            server.Start();
            System.Threading.ThreadPool.SetMinThreads(10, 10);
            var host = CreateHostBuilder(args).Build();
            host.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping.Register(() =>
                {
                    MinecraftSocket.BroadcastApplicationStopping();
                    server.Stop();
                });
            host.Run();
        }

        /// <summary>
        /// Keeps websocket-sharp's session sweeper enabled and gives it a more forgiving pong
        /// deadline. Must run before <c>AddWebSocketService</c>, since the service manager only
        /// copies WaitTime/KeepClean onto a newly added service host at that point (or through
        /// its own setters afterwards).
        ///
        /// The sweeper (WebSocketSessionManager.Sweep) runs every 60s: it pings every session that
        /// reports as Open and drops any session that is no longer Open. A session whose TCP
        /// connection dies before or while it is being registered is only ever cleaned up by this
        /// sweep. WaitTime is raised from the 1s default to 10s so slow-but-alive clients aren't
        /// dropped on the pong deadline. KeepClean used to be disabled here; that leaked dead
        /// sessions and their per-connection state until the pods OOM'd.
        /// </summary>
        internal static void ConfigureSessionCleanup(HttpServer server)
        {
            server.WaitTime = TimeSpan.FromSeconds(10);
            server.KeepClean = true;
        }

        private static async Task HandleLogRequest(HttpRequestEventArgs e)
        {
            e.Response.StatusCode = 200;
            e.Response.ContentType = "text/plain";
            e.Response.ContentEncoding = System.Text.Encoding.UTF8;
            var manager = DiHandler.GetService<VpsInstanceManager>();
            var timeStamp = e.Request.QueryString["timestamp"]?.ToString() ?? throw new CoflnetException("missing_query", "missing timestamp query");
            var user = e.Request.QueryString["user"]?.ToString();
            var token = e.Request.QueryString["token"];
            var logContent = await manager.GetLog(token, long.Parse(timeStamp), user);
            var response = Encoding.UTF8.GetBytes(logContent);

            // Add headers for file download
            e.Response.Headers.Add("Content-Disposition", "attachment; filename=instance.log");

            e.Response.ContentLength64 = response.Length;
            Console.WriteLine($"Sending log file download of {response.Length} bytes");
            e.Response.Close(response, true);
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((_, config) => config.AddOpenBaoFromEnvironment())
                // Shared OTel logging configuration from Coflnet.Core.
                // Bridges ILogger -> OTLP (HttpProtobuf) with trace-log correlation,
                // k8s pod attributes, and DEV_LOGGING console fallback.
                .ConfigureLogging((context, logging) => logging.AddOpenTelemetryLogging(
                    context.Configuration,
                    context.Configuration["JAEGER_SERVICE_NAME"] ?? "sky-commands-mod"))
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        private static TaskFactory factory = new TaskFactory();
        public static void RunIsolatedForever(Func<Task> todo, string message, int backoff = 2000)
        {
            factory.StartNew(async () =>
            {
                await Task.Delay(1000).ConfigureAwait(false);
                while (true)
                {
                    try
                    {
                        await todo();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"{message}: {e.Message} {e.StackTrace}\n {e.InnerException?.Message} {e.InnerException?.StackTrace} {e.InnerException?.InnerException?.Message} {e.InnerException?.InnerException?.StackTrace}");
                        await Task.Delay(2000).ConfigureAwait(false);
                    }
                    await Task.Delay(backoff).ConfigureAwait(false);
                }
            }, TaskCreationOptions.LongRunning).ConfigureAwait(false);
        }
    }
}
