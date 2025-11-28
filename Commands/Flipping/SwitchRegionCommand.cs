using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WebSocketSharp;

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
            socket.Dialog(db => db.MsgLine($"Switching to {McColorCodes.AQUA}EU"));
            socket.AccountInfo.Region = "eu";
            await socket.sessionLifesycle.AccountInfo.Update();
            socket.ExecuteCommand("/cofl connect ws://sky-mod.coflnet.com/modsocket");
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
            //socket.ExecuteCommand("/cofl connect ws://sky-us.coflnet.com/modsocket");
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
        var protocol = !Version.TryParse(socket.Version, out var version) || version < new Version(1, 7, 9) ? "ws" : "wss";

        if (!string.IsNullOrEmpty(clientIp) && linodePrefixes.Any(clientIp.StartsWith))
        {
            socket.Dialog(db => db.MsgLine("You seem have good connection to linode, switching to us-linode"));
            tobeUsed = "us-linode";
        }
        // check twice if the server is reachable
        if (await CheckReachable(tobeUsed) || await CheckReachable(tobeUsed))
        {
            socket.Dialog(db => db.MsgLine("Switching to us server"));
            socket.ExecuteCommand($"/cofl connect {protocol}://{tobeUsed}.coflnet.com/modsocket");
            return;
        }
        if (tobeUsed != MainUs && await CheckReachable(MainUs))
        {
            socket.Dialog(db => db.MsgLine("Switching to us server"));
            socket.ExecuteCommand($"/cofl connect {protocol}://{MainUs}.coflnet.com/modsocket");
            return;
        }
        if (tobeUsed == MainUs && await CheckReachable("us-linode"))
        {
            socket.Dialog(db => db.MsgLine("Switching to us-linode server"));
            socket.ExecuteCommand($"/cofl connect {protocol}://us-linode.coflnet.com/modsocket");
            return;
        }

        socket.Dialog(db => db.MsgLine("US server seems to be currently not reachable :(").MsgLine("We are probably trying to get them online again, you stay connected to eu in the meantime, sorry"));

    }

    private static async Task<bool> CheckReachable(string tobeUsed)
    {
        // timeout after 5 seconds
        var restClient = new RestSharp.RestClient($"http://{tobeUsed}.coflnet.com");
        var request = new RestSharp.RestRequest("/modsocket")
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var response = await restClient.ExecuteAsync(request);
        var reachable = response.StatusCode != 0 && response.StatusCode != System.Net.HttpStatusCode.NotFound;
        return reachable;
    }
}