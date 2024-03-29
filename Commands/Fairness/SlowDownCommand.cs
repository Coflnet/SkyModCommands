using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Allows you to slow down some user")]
public class SlowDownCommand : McCommand
{
    private static HashSet<string> _slowDowns = new HashSet<string>();

    public static bool IsSlowedDown(string uuid)
    {
        return _slowDowns.Contains(uuid);
    }
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var csrf = socket.SessionInfo.ConnectionId;
        var args = arguments.Trim('"').Split(' ');
        var userName = args[0];
        if(args.Length == 1)
        {
            var productsApi = socket.GetService<ProductsApi>();
            var product = await productsApi.ProductsPProductSlugGetAsync("slowdown");
            socket.Dialog(db => db.CoflCommand<SlowDownCommand>(
                    $"Confirm that you want to slow down {McColorCodes.AQUA}{userName}{McColorCodes.RESET} for {product.Cost} CoflCoins", 
                    $"{userName} {csrf}", $"Slow down {userName}"));
            return;
        }
        if (args.Length < 1|| args.Length == 2 && args[1] != csrf)
        {
            socket.Dialog(db => db.MsgLine($"You need to specify a player. eg. {McColorCodes.AQUA}/cofl slowdown <ign>"));
            return;
        }
        var uuid = await socket.GetPlayerUuid(userName);
        var userApi = socket.GetService<UserApi>();
        if (!await PurchaseCommand.Purchase(socket, userApi, "slowdown", 1))
        {
            socket.Dialog(db => db.MsgLine("That didn't work out, please try again or report this"));
            return;
        }
        _slowDowns.Add(uuid);
        socket.Dialog(db => db.MsgLine($"You have slowed down {userName}"));
        await Task.Delay(1000 * 62); // TODO: sync with others
        _slowDowns.Remove(uuid);
    }
}