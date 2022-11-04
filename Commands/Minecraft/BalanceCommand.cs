using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;

namespace Coflnet.Sky.Commands.MC
{
    public class BalanceCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var userApi = socket.GetService<UserApi>();
            var user = await userApi.UserUserIdGetAsync(socket.UserId);
            socket.Dialog(db => db.Msg($"Your current balance is {McColorCodes.AQUA}{socket.formatProvider.FormatPrice((long)user.Balance)} cofl coins"));
        }
    }
}