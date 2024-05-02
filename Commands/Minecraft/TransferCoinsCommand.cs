using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;
public class TransferCoinsCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        var amount = 0;
        var minecraftName = "";
        try
        {
            amount = int.Parse(args[0]);
            minecraftName = args[1];
        }
        catch
        {
            throw new CoflnetException("invalid_arguments", "Usage /cofl transfercoins <amount> <user>");
        }
        string targetUser = await GetUserIdFromMcName(socket, minecraftName);

        var userApi = socket.GetService<IUserApi>();
        try
        {
            var currentUserId = socket.sessionLifesycle.UserId.Value ?? throw new CoflnetException("not_logged_in", "You need to be logged in to transfer coins");
            var transaction = await userApi.UserUserIdTransferPostAsync(currentUserId, new Coflnet.Payments.Client.Model.TransferRequest()
            {
                Amount = amount,
                Reference = minecraftName + "-" + socket.SessionInfo.ConnectionId.Truncate(5),
                TargetUser = targetUser
            });
            socket.Dialog(db => db.MsgLine($"You sent {amount} coins to {minecraftName}"));
        }
        catch (Payments.Client.Client.ApiException ex)
        {
            throw new CoflnetException("payment_error", ex.Message.Substring("Error calling UserUserIdTransferPost: {.Message.:".Length));
        }
    }

}