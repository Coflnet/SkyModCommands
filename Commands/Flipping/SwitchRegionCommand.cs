using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Switches your region", "currently supported: eu, us")]
public class SwitchRegionCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var selected = JsonConvert.DeserializeObject<string>(arguments).ToLower().Trim();
        if (string.IsNullOrEmpty(selected))
        {
            socket.Dialog(db => db.MsgLine($"Current region: {McColorCodes.AQUA}{socket.AccountInfo.Region}")
                .MsgLine($"Usage: /cofl switchRegion <region>")
                .MsgLine($"Available Regions: {McColorCodes.AQUA}eu, us")
                .CoflCommand<SwitchRegionCommand>("Click to switch", socket.AccountInfo.Region == "us" ? "eu" : "us", "Click to switch your region"));
            return;
        }
        if (socket.SessionInfo.IsMacroBot && socket.Version.StartsWith("1.5.0"))
        {
            socket.Dialog(db => db.MsgLine($"Your client does not appear to support reconnecting to another server"));
            return;
        }
        if (selected == "eu")
        {
            socket.AccountInfo.Region = "eu";
            await socket.sessionLifesycle.AccountInfo.Update();

            if (ModSessionLifesycle.UsesDirectConnectionType(socket.SessionInfo.ConnectionType))
            {
                socket.Dialog(db => db.MsgLine("Already connected to eu server"));
                return;
            }

            socket.Dialog(db => db.MsgLine($"Switching to {McColorCodes.AQUA}EU"));
            socket.ExecuteCommand("/cofl connect wss://sky.coflnet.com/modsocket");
        }
        else if (selected == "us")
        {
            if (await socket.UserAccountTier() < Shared.AccountTier.PREMIUM_PLUS)
            {
                socket.Dialog(db => db.CoflCommand<PurchaseCommand>($"You need to be {McColorCodes.AQUA}Premium+{McColorCodes.WHITE} to use the US region. [click to upgrade]", "prem+", "Click to upgrade"));
                return;
            }
            socket.Dialog(db => db.MsgLine($"Switching to {McColorCodes.AQUA}US"));
            socket.AccountInfo.Region = "us";
            await socket.sessionLifesycle.AccountInfo.Update();

            await TryToConnect(socket);
        }
        else
        {
            socket.Dialog(db => db.MsgLine($"Unknown region `{McColorCodes.AQUA}{selected}`"));
        }
        return;
    }

    private static readonly string MainUs = "sky-us";
    public static async Task TryToConnect(MinecraftSocket socket)
    {
        var tobeUsed = MainUs;
        var clientIp = socket.ClientIp;
        var linodePrefixes = new List<string> {
            "172.23",
            "130.131", //azure
        };
        var protocol = "wss"; // Always use secure WebSocket now that 1.7.9 version can't join hypixel anymore

        if (!string.IsNullOrEmpty(clientIp) && linodePrefixes.Any(clientIp.StartsWith))
        {
            socket.Dialog(db => db.MsgLine("You seem have good connection to linode, switching to us-linode"));
            tobeUsed = "us-linode";
        }
        // Retry the preferred region once, then try the other region.
        var candidates = new[] { tobeUsed, tobeUsed, tobeUsed == MainUs ? "us-linode" : MainUs };
        foreach (var host in candidates)
        {
            var endpoint = new Uri($"{protocol}://{host}.coflnet.com/modsocket");
            if (!await CheckReachable(endpoint))
                continue;

            socket.Dialog(db => db.MsgLine("Switching to us server"));
            socket.ExecuteCommand($"/cofl connect {endpoint}");
            return;
        }

        socket.Dialog(db => db.MsgLine("US server seems to be currently not reachable :(").MsgLine("We are probably trying to get them online again, you stay connected to eu in the meantime, sorry"));

    }

    internal static async Task<bool> CheckReachable(Uri endpoint, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new ClientWebSocket();
        try
        {
            // Plain HTTP invokes BFCS's unrelated outbound HTTP health check.
            // Verify the same WebSocket endpoint that the client will connect to.
            await client.ConnectAsync(endpoint, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
