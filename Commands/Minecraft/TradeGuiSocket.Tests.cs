using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Coflnet.Sky.Commands.MC;

public class TradeGuiSocketTests
{
    private static readonly TaskCompletionSource Shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public class FixtureSocket : MinecraftSocket
    {
        protected override void OnOpen()
        {
            StartNewConnectionSpan();
            SessionInfo.McUuid = "00000000000000000000000000000000";
            var update = Response.Create("commandUpdate", HelpCommand.BuildCommandList(Commands.Values.Where(c => c != null)));
            update.type = "commandUpdate";
            Send(update);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            string type = JObject.Parse(e.Data)["type"]?.Value<string>();
            if (type == "fixtureShutdown")
            {
                Shutdown.TrySetResult();
                return;
            }
            if (type == "tradegui" || type == "help")
                base.OnMessage(e);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            ConSpan?.Dispose();
        }
    }

    private static HttpServer StartServer(int port)
    {
        DiHandler.OverrideService<ActivitySource, ActivitySource>(new ActivitySource("TradeGuiProtocolTest"));
        var server = new HttpServer(port);
        Program.ConfigureSessionCleanup(server);
        server.AddWebSocketService<FixtureSocket>("/modsocket");
        server.Start();
        return server;
    }

    [Test]
    [CancelAfter(30000)]
    public async Task RegisteredCommandRoundTripsThroughRealWebSocket()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var server = StartServer(port);
        try
        {
            using var client = new WebSocket($"ws://127.0.0.1:{port}/modsocket");
            var updated = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            var received = new System.Collections.Concurrent.BlockingCollection<JToken>();
            client.OnMessage += (_, e) =>
            {
                var response = JsonConvert.DeserializeObject<Response>(e.Data);
                if (response.type == "commandUpdate") updated.TrySetResult(JObject.Parse(response.data));
                if (response.type == "tradeGui") received.Add(JObject.Parse(response.data)["enabled"]);
            };
            client.Connect();
            var commands = await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(commands.ContainsKey("tradegui on"), Is.True);
            Assert.That(commands.ContainsKey("tradegui off"), Is.True);
            Assert.That(commands.ContainsKey("report"), Is.True);
            foreach (string state in new[] { "on", "off", "" })
            {
                client.Send(JsonConvert.SerializeObject(new Response("tradegui", JsonConvert.SerializeObject(state))));
                Assert.That(received.TryTake(out var enabled, 8000), Is.True);
                if (state == "") Assert.That(enabled.Type, Is.EqualTo(JTokenType.Null));
                else Assert.That(enabled.Value<bool>(), Is.EqualTo(state == "on"));
            }
            client.Close();
            received.Dispose();
        }
        finally
        {
            server.Stop();
        }
    }

    [Test]
    [Explicit("Starts the backend protocol fixture for a matching Core or Minecraft client")]
    [CancelAfter(600000)]
    public async Task ExternallyDrivenProtocolFixture()
    {
        int port = int.Parse(Environment.GetEnvironmentVariable("SKYCOFL_PROTOCOL_PORT") ?? "18084");
        var server = StartServer(port);
        try
        {
            Console.WriteLine($"Trade GUI protocol fixture ready on port {port}");
            await Shutdown.Task.WaitAsync(TimeSpan.FromMinutes(9));
        }
        finally
        {
            server.Stop();
        }
    }
}
