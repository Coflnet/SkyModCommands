using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.MC;
using NUnit.Framework;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Regression tests for the connection-leak fix: <c>Program.ConfigureSessionCleanup</c> keeps
    /// websocket-sharp's session sweeper enabled (mirrors the equivalent fix and tests in the
    /// sibling SkyCommands repo's Socket/Server.Tests.cs / Socket/SkyblockBackEnd.Tests.cs).
    /// </summary>
    public class MinecraftSocketSessionCleanupTests
    {
        /// <summary>
        /// Finds a currently unused TCP port by briefly binding to port 0 and reading back
        /// the port the OS assigned. <see cref="HttpServer"/> rejects an explicit port of 0.
        /// </summary>
        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        [SetUp]
        public void Setup()
        {
            // MinecraftSocket.OnError/OnClose/Log all resolve an ActivitySource out of the shared
            // static DI container (via CreateActivity()/tracer). In production that's wired up by
            // Startup.cs; give the test process one directly so those calls don't throw with
            // "service not registered" when no Startup.cs ever ran here.
            DiHandler.OverrideService<ActivitySource, ActivitySource>(new ActivitySource("Test"));
        }

        /// <summary>
        /// Minimal MinecraftSocket subclass used only to host the end to end cleanup test below.
        /// The real MinecraftSocket.OnOpen kicks off StartConnection(), which resolves several
        /// services (IdConverter, FlipperService, the ModSessionLifesycle chain, ...) that are
        /// normally wired up by Startup.cs and are impractical to fully stand up in a unit test.
        /// OnError/OnClose - the methods this test actually exercises - don't depend on that
        /// bootstrap, so this override keeps only the part of OnOpen those paths need (starting
        /// the connection's Activity span, so ConSpan is non-null) and skips StartConnection.
        /// </summary>
        private class TestableMinecraftSocket : MinecraftSocket
        {
            protected override void OnOpen()
            {
                StartNewConnectionSpan();
            }
        }

        [Test]
        public void ConfigureSessionCleanup_EnablesSweeperWithLongerWaitTime()
        {
            var server = new HttpServer(GetFreePort());

            Program.ConfigureSessionCleanup(server);

            Assert.That(server.KeepClean, Is.True, "the sweeper must stay enabled or dead sessions leak forever");
            Assert.That(server.WaitTime, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void ConfigureSessionCleanup_PropagatesToServiceHostsAddedAfterwards()
        {
            var server = new HttpServer(GetFreePort());

            Program.ConfigureSessionCleanup(server);
            server.AddWebSocketService<TestableMinecraftSocket>("/modsocket");

            var host = server.WebSocketServices["/modsocket"];

            Assert.That(host.KeepClean, Is.True);
            Assert.That(host.WaitTime, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }

        /// <summary>
        /// End to end regression test for the leak: a client's TCP connection is killed abruptly
        /// (no close handshake, matching an app crash or a lost network path), and the server-side
        /// session must not linger. Either MinecraftSocket.OnError closing the library session, or
        /// the sweeper reaping it, is an acceptable path - the point is the session dictionary does
        /// not grow forever.
        /// </summary>
        [Test]
        [CancelAfter(20000)]
        public void DeadConnection_DoesNotLingerAsOpenSession()
        {
            var port = GetFreePort();
            var server = new HttpServer(port);
            Program.ConfigureSessionCleanup(server);
            server.AddWebSocketService<TestableMinecraftSocket>("/modsocket");
            server.Start();

            try
            {
                var host = server.WebSocketServices["/modsocket"];

                using var client = new WebSocket($"ws://127.0.0.1:{port}/modsocket?player=tester&SId=test-session&version=1.5.0-af");
                client.Connect();

                WaitUntil(() => host.Sessions.Count == 1, "server never registered the incoming session");

                KillConnectionAbruptly(client);

                WaitUntil(() =>
                {
                    // the sweeper only runs on its own 60s timer in production; call it directly
                    // here so the test does not depend on that interval.
                    host.Sessions.Sweep();
                    return host.Sessions.Count == 0;
                }, "dead session was never removed");

                Assert.That(host.Sessions.Count, Is.EqualTo(0));
            }
            finally
            {
                server.Stop();
            }
        }

        private static void WaitUntil(Func<bool> condition, string failureMessage, int timeoutMs = 10000)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return;
                Thread.Sleep(50);
            }

            Assert.Fail(failureMessage);
        }

        /// <summary>
        /// Forces the client-side TCP connection closed with an RST instead of a WebSocket close
        /// handshake, simulating a crashed client / dropped network path. websocket-sharp's client
        /// WebSocket does not expose a way to do this itself, so this reaches into its private
        /// TcpClient via reflection.
        /// </summary>
        private static void KillConnectionAbruptly(WebSocket client)
        {
            var tcpClientField = typeof(WebSocket).GetField("_tcpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tcpClient = (TcpClient)tcpClientField.GetValue(client);
            Assert.That(tcpClient, Is.Not.Null, "test relies on websocket-sharp's private _tcpClient field");

            tcpClient.LingerState = new LingerOption(true, 0); // abortive close -> sends RST instead of FIN
            tcpClient.Client.Close(0);
        }
    }
}
