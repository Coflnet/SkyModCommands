using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;
public class UpgradePlanCommand : PurchaseCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var transactionsApi = socket.GetService<ITransactionApi>();
        var userApi = socket.GetService<UserApi>();
        if (socket.sessionLifesycle.TierManager.IsLicense)
        {
            socket.Dialog(db => db.MsgLine("Your connection is using a license. Licenses can't be upgraded. If you want a higher tier you will have to buy a new one.")
                .CoflCommand<LicensesCommand>("Buy a prem+",
                    $"add {socket.SessionInfo.McName} premium_plus {socket.SessionInfo.ConnectionId}",
                    $"Click to buy a new license for {socket.SessionInfo.McName}"));
            return;
        }
        var recentTransactions = await transactionsApi.TransactionUUserIdGetAsync(socket.UserId, 0, 10);
        var timeAllowed = TimeSpan.FromHours(1);
        if (recentTransactions.Any(t => t.ProductId == "test-premium" && t.TimeStamp > DateTime.UtcNow.AddDays(-3)))
        {
            timeAllowed = TimeSpan.FromDays(3);
        }
        var lastPurchase = recentTransactions.FirstOrDefault(t => t.ProductId == "premium" && IsNotLicense(t));
        if (lastPurchase == null)
        {
            socket.Dialog(db => db.MsgLine($"You don't seem to have a premium tier that can be upgraded, if you still want it upgraded please write in our discord support channel. Upgrading may require having CoflCoins"));
            return;
        }
        if (lastPurchase.TimeStamp < DateTime.UtcNow - timeAllowed)
        {
            socket.Dialog(db => db.MsgLine($"Your premium purchase was too long ago to upgrade it. \nPlease buy prem+ directly instead, your premium will be extended by the time of prem+ you buy."));
            return;
        }

        var args = Convert<string>(arguments).Split(' ');
        if (args[0] != socket.SessionInfo.SessionId)
        {
            socket.Dialog(db => db.MsgLine($"Do you want to convert your {McColorCodes.GREEN}premium{McColorCodes.WHITE} to one week of {McColorCodes.GOLD}premium+{McColorCodes.WHITE} for {McColorCodes.AQUA}900 CoflCoins")
                .CoflCommand<UpgradePlanCommand>($"  {McColorCodes.GREEN}Yes  ", $"{socket.SessionInfo.SessionId}", $"Confirm upgrade (paying 900 CoflCoins)")
                .DialogLink<EchoDialog>($"  {McColorCodes.RED}No  ", $"Upgrade Canceled", $"{McColorCodes.RED}Cancel upgrade"));
            return;
        }
        var userInfo = await userApi.UserUserIdGetAsync(socket.UserId);
        if (userInfo.Balance < 900)
        {
            socket.Dialog(db => db.MsgLine($"You need at least 900 coins to upgrade your {McColorCodes.GREEN}premium{McColorCodes.WHITE} to one week of {McColorCodes.GOLD}premium+")
                .MsgLine($"You can buy more CoflCoins with {McColorCodes.AQUA}/cofl topup"));
            return;
        }
        await userApi.UserUserIdTransactionIdDeleteAsync(socket.UserId, int.Parse(lastPurchase.Id));
        if (await Purchase(socket, userApi, "premium_plus", 1, "upgrade premium"))

            socket.Dialog(db => db.MsgLine($"Successfully upgraded your {McColorCodes.GREEN}premium{McColorCodes.WHITE} to one week of {McColorCodes.GOLD}premium+")
                                .MsgLine("Thanks for supporting our project financially :)"));
        else
            socket.Dialog(db => db.MsgLine($"Failed to upgrade your {McColorCodes.GREEN}premium{McColorCodes.WHITE} to one week of {McColorCodes.GOLD}premium+")
                                .MsgLine("Please ask for support on our discord server."));


    }

    private static bool IsNotLicense(ExternalTransaction t)
    {
        return t.Reference.Length < "f11b78e7147a41dca4e64ac986da38a5.4eb9a5eb-8f69-425b-a9c6".Length;
    }
}

