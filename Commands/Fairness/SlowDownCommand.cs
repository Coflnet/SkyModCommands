using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using WebSocketSharp;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Allows you to slow down some user")]
public class SlowDownCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var csrf = socket.SessionInfo.ConnectionId;
        var args = arguments.Trim('"').Split(' ');
        var userName = args[0];
        if (args.Length == 1 && !userName.IsNullOrEmpty())
        {
            var productsApi = socket.GetService<ProductsApi>();
            var product = await productsApi.ProductsPProductSlugGetAsync("slowdown");
            socket.Dialog(db => db.CoflCommand<SlowDownCommand>(
                    $"Confirm that you want to slow down {McColorCodes.AQUA}{userName}{McColorCodes.RESET} for {product.Cost} CoflCoins {McColorCodes.YELLOW}(click to confirm)",
                    $"{userName} {csrf}", $"Slow down {userName}"));
            return;
        }
        if (args.Length < 1 || args.Length == 2 && args[1] != csrf)
        {
            socket.Dialog(db => db.MsgLine($"You need to specify a player. eg. {McColorCodes.AQUA}/cofl slowdown <ign>"));
            return;
        }
        var uuid = await socket.GetPlayerUuid(userName);
        var userApi = socket.GetService<UserApi>();
        if (!await PurchaseCommand.Purchase(socket, userApi, "slowdown", 1, userName + DateTime.UtcNow.ToString("hh:mm")))
        {
            socket.Dialog(db => db.MsgLine("That didn't work out, please try again or report this"));
            return;
        }
        socket.GetService<DelayService>().SlowDown(uuid);
        socket.Dialog(db => db.MsgLine($"You have slowed down {userName}", null, "will take effect on next update"));

        var userInfo = await socket.GetService<McAccountService>().GetUserId(uuid);
        try
        {
            await socket.GetService<TopUpApi>().TopUpCustomPostAsync(userInfo.ExternalId, new()
            {
                Amount = 100,
                ProductId = "compensation",
                Reference = $"slowdown from {socket.SessionInfo.McName} {DateTime.UtcNow.ToString("hh:mm")}"
            });

        }
        catch (Exception e)
        {
            dev.Logger.Instance.Error(e, "Failed to compensate for slowdown");
        }
        await Task.Delay(DelayService.DelayTime);
        socket.Dialog(db => db.MsgLine($"Slowdown for {userName} expired."));
    }
}
