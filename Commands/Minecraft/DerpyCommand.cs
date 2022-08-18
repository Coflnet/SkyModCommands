using System;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;

namespace Coflnet.Sky.Commands.MC
{
    public class DerpyCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if(arguments.Trim('"') == "time")
            {
                var userApi = socket.GetService<UserApi>();
                await userApi.UserUserIdServicePurchaseProductSlugPostAsync(socket.sessionLifesycle.UserId, "premium-derpy", "modCommand" + DateTime.Now.Date.ToShortDateString());
                socket.SendMessage(COFLNET + $"You purchased 5 days of premium with your derpy compensation");
                return;
            }
            socket.SendMessage(COFLNET + $"Hello there, this command allows you to purchase premium for 5 days with your derpy compensation.\nTo purchase premium, type {McColorCodes.AQUA}/cofl derpy time");
        }
    }
}