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
                .MsgLine("§7Your internet will be used to fetch public web pages."));
            return;
        }

        // Subcommands: exchange, list, on/off
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0] : string.Empty;

        if (verb == "exchange" || verb == "convert" || verb == "ex")
        {
            var tier = parts.Length > 1 ? parts[1] : "best";
            await HandleExchange(socket, tier);
            return;
        }

        if (verb == "list" || verb == "points")
        {
            await HandleList(socket);
            return;
        }

        if (verb == "on" || verb == "enable" || verb == "yes" || verb == "off" || verb == "disable" || verb == "no")
        {
            await HandleToggle(socket, verb);
            return;
        }

        
    }

    private async Task HandleList(MinecraftSocket socket)
    {
        var ps = socket.GetService<ModCommands.Services.ProxyService>();
        var points = await ps.GetPointsAsync(socket.UserId);
        socket.Dialog(db => db.MsgLine($"§7You have §e{points} §7proxy points.")
            .MsgLine("§7Exchange options: small=2000->4, medium=20000->50, large=200000->600, giant=2000000->7000")
            .MsgLine("§7Use §e/cofl proxy exchange <tier> §7or §e/cofl proxy exchange§7 for best automatic conversion."));
    }

    private async Task HandleExchange(MinecraftSocket socket, string tierArg)
    {
        var tier = tierArg?.ToLower() ?? "best";
        var ps = socket.GetService<ModCommands.Services.ProxyService>();
        long pointsLong = await ps.GetPointsAsync(socket.UserId);

        if (pointsLong <= 0)
        {
            socket.Dialog(db => db.MsgLine("§cYou have no proxy points to exchange."));
            return;
        }

        // Define tiers
        var tiers = new[] {
            new { Name = "small", Points = 2000L, Coins = 4 },
            new { Name = "medium", Points = 20000L, Coins = 50 },
            new { Name = "large", Points = 200000L, Coins = 600 },
            new { Name = "giant", Points = 2000_000L, Coins = 7000 }
        };

        long totalCoins = 0;
        long pointsToConsume = 0;

        if (tiers.Any(t => t.Name == tier))
        {
            var chosen = tiers.First(t => t.Name == tier);
            var count = pointsLong / chosen.Points;
            if (count == 0)
            {
                socket.Dialog(db => db.MsgLine($"§cYou need at least {chosen.Points} points for the {chosen.Name} exchange."));
                return;
            }
            totalCoins = count * chosen.Coins;
            pointsToConsume = count * chosen.Points;
        }
        else // best
        {
            var remaining = pointsLong;
            foreach (var t in tiers.Reverse())
            {
                var count = remaining / t.Points;
                if (count > 0)
                {
                    totalCoins += count * t.Coins;
                    pointsToConsume += count * t.Points;
                    remaining -= count * t.Points;
                }
            }
            if (totalCoins == 0)
            {
                socket.Dialog(db => db.MsgLine("§cYou don't have enough points for any exchange tier."));
                return;
            }
        }

        try
        {
            var topup = socket.GetService<TopUpApi>();
            // include current ISO week (e.g. "2025-01") so the server can enforce one conversion per week
            var now = DateTime.UtcNow;
            var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
            var weekNo = cal.GetWeekOfYear(now, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            var weekKey = $"{now.Year:D4}-{weekNo:D2}";

            await topup.TopUpCustomPostAsync(socket.UserId, new()
            {
                ProductId = "proxy_exchange",
                Amount = (int)totalCoins,
                Reference = "proxy-exchange-" + weekKey
            });

            // Deduct points
            await ps.AdjustPointsAsync(socket.UserId, -pointsToConsume);

            socket.Dialog(db => db.MsgLine($"§aExchanged {pointsToConsume} proxy points for {totalCoins} CoflCoins."));
        }
        catch (Exception ex)
        {
            socket.GetService<ILogger<ProxyCommand>>().LogError(ex, "Error during proxy exchange topup");
            socket.Dialog(db => db.MsgLine("§cAn error occurred while exchanging points. Please contact support."));
        }
    }

    private async Task HandleToggle(MinecraftSocket socket, string verb)
    {
        bool optIn;
        if (verb == "on" || verb == "enable" || verb == "yes")
        {
            optIn = true;
            socket.Dialog(db => db.MsgLine("§aProxy enabled!")
                .MsgLine("§7You will now receive proxy requests.")
                .MsgLine("§7Thank you for helping with data collection!"));
        }
        else if (verb == "off" || verb == "disable" || verb == "no")
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
        if (accInfo != null)
        {
            if (accInfo.ProxyOptIn)
                proxyService.RegisterSocket(socket);
            else
                proxyService.UnregisterSocket(socket);
        }
    }
}
