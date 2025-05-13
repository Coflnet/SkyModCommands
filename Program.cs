using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using WebSocketSharp.Server;
using Coflnet.Sky.Commands.MC;
using System.Threading.Tasks;
using System;
using Coflnet.Sky.ModCommands.Services.Vps;
using Coflnet.Sky.Commands.Shared;
using System.Text;
using Coflnet.Sky.Core;

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
            server.KeepClean = false;
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

            _ = Core.ItemDetails.Instance.LoadLookup();

            CreateHostBuilder(args).Build().Run();
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
