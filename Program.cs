using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using WebSocketSharp.Server;
using Coflnet.Sky.Commands.MC;
using System.Threading.Tasks;
using System;
using Coflnet.Sky.ModCommands.Services;

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
            server.Log.Level = WebSocketSharp.LogLevel.Debug;
            server.Log.Output = (data, s) => Console.WriteLine(data);
            server.OnGet += async (s, e) =>
            {
                await Task.Delay(1).ConfigureAwait(false);
                e.Response.StatusCode = 201;
            };
            server.Start();
            System.Threading.ThreadPool.SetMinThreads(10, 10);

            _ = Core.ItemDetails.Instance.LoadLookup();

            CreateHostBuilder(args).Build().Run();
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
