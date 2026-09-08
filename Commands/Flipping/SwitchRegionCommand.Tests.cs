using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Tests;

public class SwitchRegionCommandTests
{
    [TestCase(101, true)]
    [TestCase(200, false)]
    [TestCase(500, false)]
    public async Task ReachabilityRequiresAWebSocketUpgrade(int status, bool expected)
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var endpoint = new Uri($"ws://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/modsocket");
        var probe = SwitchRegionCommand.CheckReachable(endpoint);
        using var connection = await server.AcceptTcpClientAsync();
        using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        Assert.That(await reader.ReadLineAsync(), Is.EqualTo("GET /modsocket HTTP/1.1"));
        string key = null;
        bool upgrade = false;
        string line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                key = line.Split(':', 2)[1].Trim();
            if (line.Equals("Upgrade: websocket", StringComparison.OrdinalIgnoreCase))
                upgrade = true;
        }
        Assert.That(upgrade, Is.True);
        Assert.That(key, Is.Not.Null);
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var headers = status == 101
            ? $"Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n"
            : "Content-Length: 0\r\nConnection: close\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\n{headers}\r\n"));
        Assert.That(await probe, Is.EqualTo(expected));
    }

    [Test]
    public async Task UnresponsiveEndpointCanBeCancelled()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var cancellation = new CancellationTokenSource();
        var endpoint = new Uri($"ws://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/modsocket");
        var probe = SwitchRegionCommand.CheckReachable(endpoint, cancellation.Token);
        using var connection = await server.AcceptTcpClientAsync();
        cancellation.Cancel();
        Assert.That(await probe.WaitAsync(TimeSpan.FromSeconds(2)), Is.False);
    }
}
