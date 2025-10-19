using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Manage proxy opt-in status",
    "Use '/cofl proxy on' to enable proxying requests",
    "Use '/cofl proxy off' to disable proxying requests",
    "When enabled, you help by proxying web requests for data collection")]
public class ProxyCommand : McCommand
{
    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = Convert<string>(arguments)?.ToLower().Trim();
        Console.WriteLine($"ProxyCommand executed with args: {args}");

        if (string.IsNullOrEmpty(args))
        {
            // Show current status from account info
            var accountInfo = socket.sessionLifesycle.AccountInfo?.Value;
            var currentStatus = accountInfo?.ProxyOptIn ?? false;

            socket.Dialog(db => db.MsgLine($"§7Proxy status: {(currentStatus ? "§aEnabled" : "§cDisabled")}")
                .MsgLine("§7Use §e/cofl proxy on §7to enable or §e/cofl proxy off §7to disable")
                .MsgLine("§7When enabled, you help by proxying web requests for data collection.")
                .MsgLine("§7Your connection will be used to fetch public web pages."));
            return;
        }

        bool optIn;
        if (args == "on" || args == "enable" || args == "yes")
        {
            optIn = true;
            socket.Dialog(db => db.MsgLine("§aProxy enabled!")
                .MsgLine("§7You will now receive proxy requests.")
                .MsgLine("§7Thank you for helping with data collection!"));
        }
        else if (args == "off" || args == "disable" || args == "no")
        {
            optIn = false;
            socket.Dialog(db => db.MsgLine("§cProxy disabled!")
                .MsgLine("§7You will no longer receive proxy requests."));
        }
        else
        {
            socket.Dialog(db => db.MsgLine("§cInvalid argument. Use §eon §cor §eoff§c."));
            return;
        }

        // Save to account data
        var accInfo = socket.sessionLifesycle.AccountInfo?.Value;
        if (accInfo != null)
        {
            accInfo.ProxyOptIn = optIn;
            await socket.sessionLifesycle.AccountInfo.Update(accInfo);
        }

        // Register or unregister with proxy service
        var proxyService = socket.GetService<ModCommands.Services.ProxyService>();
        // Register or unregister with proxy service using current account info
        if (accInfo != null)
        {
            if (accInfo.ProxyOptIn)
                proxyService.RegisterSocket(socket);
            else
                proxyService.UnregisterSocket(socket);
        }
    }
}
