using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

public class VerifyCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (socket.AccountInfo == null)
        {
            socket.SendMessage("You are not logged in. Please log in first.");
            await Task.Delay(4000);
            socket.ModAdapter.SendLoginPrompt(socket.sessionLifesycle.GetAuthLink(socket.SessionInfo.SessionId));
            return;
        }
        var verifcationHandler = socket.sessionLifesycle.VerificationHandler;
        var isVerified = await verifcationHandler.CheckVerificationStatus(socket.AccountInfo);
        if (isVerified)
        {
            socket.SendMessage($"You are verified. You can chat with {McColorCodes.AQUA}/fc <message>");
            return;
        }
        socket.Dialog(db => db.MsgLine("You are not verified yet. Please follow the instructions above to verify your account."));
    }
}
