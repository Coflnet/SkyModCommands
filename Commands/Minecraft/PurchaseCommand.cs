using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class PurchaseCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var productApi = socket.GetService<ProductsApi>();
            var topUpApi = socket.GetService<TopUpApi>();
            var userApi = socket.GetService<UserApi>();

            var parts = arguments.Trim('"').Split(' ');
            var productSlug = parts[0];

            var product = await productApi.ProductsPProductSlugGetAsync(productSlug);
            if (product == null)
            {
                socket.SendMessage(new DialogBuilder().MsgLine($"The product {productSlug} could not be fund"));
                return;
            }

            var count = 1;
            if (parts.Length > 1)
                if (!int.TryParse(parts[1], out count))
                {
                    socket.SendMessage(new DialogBuilder().MsgLine($"The count argument after {productSlug} has to be a number from 1-12"));
                    return;
                }
            if (parts.Length < 3 || parts[2] != socket.SessionInfo.ConnectionId)
            {
                socket.SendMessage(new DialogBuilder()
                    .MsgLine($"Do you want to buy the product {product.Title} {count}x for a total of {socket.FormatPrice((long)product.Cost)} cofl coins")
                    .CoflCommand<PurchaseCommand>($"  {McColorCodes.GREEN}Yes  ", $"{productSlug} {count} {socket.SessionInfo.ConnectionId}", $"Confirm purchase (paying {socket.FormatPrice((long)product.Cost)} cofl coins)")
                    .DialogLink<EchoDialog>($"  {McColorCodes.RED}No  ", $"Purchase Canceled", $"{McColorCodes.RED}Cancel purchase"));
                return;
            }

            var targetConId = parts[2];
            if (targetConId != socket.SessionInfo.ConnectionId)
                throw new Coflnet.Sky.Core.CoflnetException("no_conid_match", "The purchase was started on a different connection. To prevent loss of coins please start over again.");

            try
            {
                var userInfo = await userApi.UserUserIdServicePurchaseProductSlugPostAsync(socket.UserId, productSlug, socket.SessionInfo.ConnectionId, count);
                socket.Dialog(db => db.MsgLine($"Successfully purchased {productSlug}"));
            } catch (Coflnet.Payments.Client.Client.ApiException e)
            {
                socket.SendMessage(e.ToString());
            }

        }
    }
}