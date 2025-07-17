using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Helps you verify your minecraft account",
    "This command checks if your minecraft account is verified",
    "If it is not, it will prompt you to verify it",
    "You can also use this command to check if you are verified")]
public class VerifyCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (socket.AccountInfo?.UserId == null)
        {
            await socket.SendLoginPrompt();
            return;
        }
        if (socket.sessionLifesycle?.UserId?.Value == null)
        {
            socket.Dialog(db => db.MsgLine("To refresh your login your are being reconnected"));
            socket.ExecuteCommand("/cofl start");
            await Task.Delay(500);
            socket.Close();
            return;
        }
        var verifcationHandler = socket.sessionLifesycle.VerificationHandler;
        var isVerified = await verifcationHandler.CheckVerificationStatus(socket.AccountInfo);
        if (isVerified)
        {
            var transactions = await socket.GetService<ITransactionApi>().TransactionUUserIdGetAsync(socket.UserId.ToString(), 0, 50);
            var canSendCoins = transactions.Count > 10 || transactions.Any(t => t.ProductId == "verify_mc") || transactions.Any(t => t.ProductId.Contains("premium_plus"));
            socket.SendMessage($"You are verified. You can chat with {McColorCodes.AQUA}/fc <message>");
            if (!canSendCoins)
            {
                socket.SendMessage("You don't qualify for the sending of CoflCoins tho. This happens when you use multiple emails with the same minecraft account which is not allowed. This limitation goes away if you buy prem+");
            }
            return;
        }
        socket.Dialog(db => db.MsgLine("You are not verified yet. Please follow the instructions above to verify your account."));
    }
}
