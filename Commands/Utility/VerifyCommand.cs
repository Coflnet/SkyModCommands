using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;

namespace Coflnet.Sky.Commands.MC;

public class VerifyCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (socket.AccountInfo == null)
        {
            await socket.SendLoginPrompt();
            return;
        }
        var verifcationHandler = socket.sessionLifesycle.VerificationHandler;
        var isVerified = await verifcationHandler.CheckVerificationStatus(socket.AccountInfo);
        if (isVerified)
        {
            var transactions = await socket.GetService<ITransactionApi>().TransactionUUserIdGetAsync(socket.UserId.ToString(), 0, 50);
            var canSendCoins = transactions.Count > 10 || transactions.Any(t => t.ProductId == "verify_mc") || transactions.Any(t => t.ProductId == "premium_plus");
            socket.SendMessage($"You are verified. You can chat with {McColorCodes.AQUA}/fc <message>");
            if (!canSendCoins)
            {
                socket.SendMessage("You don't qualify for the sending of CoflCoins tho. This happens when you use multiple emails with the same minecraft account which is not allowed.");
            }
            return;
        }
        socket.Dialog(db => db.MsgLine("You are not verified yet. Please follow the instructions above to verify your account."));
    }
}
