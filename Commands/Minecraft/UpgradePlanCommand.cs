using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;
public class UpgradePlanCommand : PurchaseCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var transactionsApi = socket.GetService<ITransactionApi>();
        var userApi = socket.GetService<UserApi>();
        var recentTransactions = await transactionsApi.TransactionUUserIdGetAsync(socket.UserId, 0, 10);
        var timeAllowed = TimeSpan.FromHours(1);
        if (recentTransactions.Any(t => t.ProductId == "test-premium" && t.TimeStamp > DateTime.UtcNow.AddDays(-3)))
        {
            timeAllowed = TimeSpan.FromDays(3);
        }
        var lastPurchase = recentTransactions.FirstOrDefault(t => t.ProductId == "premium" && t.TimeStamp > DateTime.UtcNow - timeAllowed);
        if (lastPurchase == null)
        {
            socket.Dialog(db => db.MsgLine($"Your premium purchase was too long ago to upgrade it. \nPlease buy prem+ directly instead, your premium will be extended by the time of prem+ you buy."));
            return;
        }
        var args = Convert<string>(arguments).Split(' ');
        if (args[0] != socket.SessionInfo.SessionId)
        {
            socket.Dialog(db => db.MsgLine($"Do you want to convert your {McColorCodes.GREEN}premium{McColorCodes.WHITE} to {McColorCodes.GOLD}premium+{McColorCodes.WHITE} for {McColorCodes.AQUA}0 coins")
                .CoflCommand<UpgradePlanCommand>($"  {McColorCodes.GREEN}Yes  ", $"{socket.SessionInfo.SessionId}", $"Confirm upgrade (paying no coins)")
                .DialogLink<EchoDialog>($"  {McColorCodes.RED}No  ", $"Upgrade Canceled", $"{McColorCodes.RED}Cancel upgrade"));
            return;
        }
        await userApi.UserUserIdTransactionIdDeleteAsync(socket.UserId, int.Parse(lastPurchase.Id));
        await Purchase(socket, userApi, "premium_plus", 1, "upgrade premium");
        socket.Dialog(db => db.MsgLine($"Successfully upgraded your {McColorCodes.GREEN}premium{McColorCodes.WHITE} to {McColorCodes.GOLD}premium+"));

    }
}

