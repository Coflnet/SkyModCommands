using System;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;

namespace Coflnet.Sky.Commands.MC
{
    public class DerpyCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (arguments.Trim('"') == "time")
            {
                var userApi = socket.GetService<UserApi>();
                try
                {
                    await userApi.UserUserIdServicePurchaseProductSlugPostAsync(socket.sessionLifesycle.UserId, "premium-derpy", "modCommand" + DateTime.Now.Date.ToShortDateString());
                    socket.SendMessage(COFLNET + $"You purchased 5 days of premium with your derpy compensation");
                }
                catch (Payments.Client.Client.ApiException e)
                {
                    if (e.Message.Contains("same reference"))
                        socket.SendMessage(COFLNET + $"You already bought 5 days of premium today, the second execution is blocked to prevent you from accidentally spending more than 400 coins");
                    else
                        socket.SendMessage(COFLNET + $"Could not buy 5 day premium because payment system returned an error: {McColorCodes.RED}{e.Message.Substring(57).TrimEnd('}', '"')}");
                }
                return;
            }
            socket.SendMessage(COFLNET + $"Hello there, this command allows you to purchase premium for 5 days with your derpy compensation.\nTo purchase premium, type {McColorCodes.AQUA}/cofl derpy time");
        }
    }
}
