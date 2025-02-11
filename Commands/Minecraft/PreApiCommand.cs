using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;
[CommandDescription("Pre api submenu", "Usage: /cofl preapi <notify|profit>")]
public class PreApiCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = JsonConvert.DeserializeObject<string>(arguments).Split(' ');
        var preapiService = socket.GetService<PreApiService>();
        if (args[0] == "notify")
        {
            var notifyAtCount = preapiService.PreApiUserCount - 1;
            if (args.Length > 1)
            {
                if (!int.TryParse(args[1], out notifyAtCount))
                {
                    socket.SendMessage($"{COFLNET}{McColorCodes.RED}Invalid number for /cofl preapi notify ");
                    return;
                }
            }
            preapiService.AddNotify(notifyAtCount, socket);
            socket.SendMessage($"{COFLNET}{McColorCodes.GREEN}You will be notified when there are less than {preapiService.PreApiUserCount} users using pre-api");
            return;
        }
        if (args[0] == "profit")
        {
            await SendBackLastDaysProfit(socket);
            return;
        }
        if (await socket.UserAccountTier() >= Shared.AccountTier.SUPER_PREMIUM)
        {
            var updated = await socket.sessionLifesycle.TierManager.GetCurrentTierWithExpire(true);
            var time = updated.expiresAt.Subtract(DateTime.UtcNow).ToString(@"mm\:ss");
            socket.Dialog(db => db.MsgLine($"Your pre-api expires in {McColorCodes.AQUA}{time}", null, "Thats minutes:seconds")
                .CoflCommand<PurchaseCommand>($"{McColorCodes.GREEN}[{McColorCodes.GRAY}Extend pre-api{McColorCodes.GREEN}]", "pre_api", "Click to extend pre-api"));
            return;
        }
        socket.Dialog(db => db.CoflCommand<PurchaseCommand>(
            $"{McColorCodes.GOLD}You currently don't have {McColorCodes.RED}pre-api\n"
            + $"{McColorCodes.YELLOW}You can click {McColorCodes.AQUA}here{McColorCodes.YELLOW} to purchase it\n",
            "pre_api", $"Click to purchase pre-api")
            .If(() => preapiService.PreApiUserCount == 0,
                db => db.MsgLine("There is nobody using pre-api currently."),
                db => db.CoflCommand<PreApiCommand>($"{McColorCodes.GREEN}[{McColorCodes.WHITE}notify me when there are less than {preapiService.PreApiUserCount} users using it{McColorCodes.GREEN}]", "notify", "Click to get notified"))
                .Break
            .CoflCommand<PreApiCommand>($"{McColorCodes.GREEN}[{McColorCodes.GRAY}Get last days profit{McColorCodes.GREEN}]", "profit", "Click to see the profit"));
        socket.Dialog(db => db.MsgLine("Pre api is finds about 1/10th of new auctions created. \nAll your delay is removed every 5 minutes until your next purchase and reduced by 30% until pre api expires"));
        if (socket.AccountInfo.Tier >= Shared.AccountTier.SUPER_PREMIUM)
        {
            var licenseCommand = $"/cofl license default {socket.SessionInfo.McName}";
            socket.Dialog(db => db.MsgLine($"Your account seems to own pre-api, but you didn't select your current ign to use it. Run {McColorCodes.AQUA}{licenseCommand}{McColorCodes.WHITE} to change that", licenseCommand, "Click to set your current ign"));
        }
    }

    private static async Task SendBackLastDaysProfit(MinecraftSocket socket)
    {
        socket.SendMessage($"Loading up all flips of the last day and calculating the profit...", null, "Can take a few seconds");
        var profit = await socket.GetService<FlipTrackingService>().GetPreApiProfit();
        var white = McColorCodes.WHITE;
        socket.Dialog(db => db.MsgLine($"A total of {McColorCodes.AQUA}{profit.PlayerCount}{white} users earned at least {McColorCodes.AQUA}{socket.FormatPrice(profit.Profit)} coins{white}. \n"
            + $"All flips bought had an estimated profit of {McColorCodes.AQUA}{socket.FormatPrice(profit.ProfitSent)}\n"
            + $"Average profit per hour was {McColorCodes.AQUA}{socket.FormatPrice(profit.ProfitSent / Math.Max(1, profit.HourCount))} {McColorCodes.GRAY}(based on sniper or median)\n"
            + $"The user with the most profit made {McColorCodes.AQUA}{socket.FormatPrice(profit.MostUserProfit)}", null,
            $"Profit per flip: {McColorCodes.AQUA}{socket.FormatPrice(profit.Profit / Math.Max(1, profit.FlipCount))}\n"
            + $"Profit per player: {McColorCodes.AQUA}{socket.FormatPrice(profit.Profit / Math.Max(1, profit.PlayerCount))}\n"
            + $"Profit per hour: {McColorCodes.AQUA}{socket.FormatPrice(profit.Profit / Math.Max(1, profit.HourCount))}\n"
            + $"Best flip: {profit.BestProfitName} {McColorCodes.GREEN}+{socket.FormatPrice(profit.BestProfit)}\n"
            ));
    }
}
