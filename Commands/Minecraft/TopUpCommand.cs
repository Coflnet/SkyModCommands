using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class TopUpCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var productApi = socket.GetService<ProductsApi>();
            var topUpApi = socket.GetService<TopUpApi>();
            var userApi = socket.GetService<UserApi>();

            var info = await topUpApi.TopUpStripePostAsync(socket.UserId, "s_cc_1800");
            socket.SendMessage(new DialogBuilder().Msg("Click this to start the payment", info.DirctLink, "open link"));
        }
    }
}