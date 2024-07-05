using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

public class VerifyCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
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
