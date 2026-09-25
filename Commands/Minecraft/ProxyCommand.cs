using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Microsoft.Extensions.Logging;
using Coflnet.Payments.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Manage proxy opt-in status",
    "Use '/cofl proxy on' to enable proxying requests",
    "Use '/cofl proxy off' to disable proxying requests",
    "Use '/cofl proxy list' to view your accrued proxy points and exchange options",
    "Use '/cofl proxy exchange' to convert points to CoflCoins",
    "When enabled, you help by proxying web requests for data collection")]
public class ProxyCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(d=>d.MsgLine("proxying feature got removed from the mod"));
    }
}
