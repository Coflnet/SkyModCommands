using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
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

            var toBuy = arguments.Trim('"');
            if (string.IsNullOrEmpty(toBuy))
            {
                var db = DialogBuilder.New;
                var topups = await productApi.ProductsTopupGetAsync(0, 100);
                db.MsgLine("Topup using paypal");
                AddOptionsFor(socket, "p", db, topups);
                db.Break.MsgLine("Topup using stripe");
                AddOptionsFor(socket, "s", db, topups);
                socket.SendMessage(db);
                return;
            }
            socket.SendMessage(new DialogBuilder().Msg($"Contacting payment provider", null, "Can take a few seconds"));
            var info = await topUpApi.TopUpStripePostAsync(socket.UserId, toBuy, new()
            {

            });
            var separationLines = "--------------------\n";
            socket.SendMessage(new DialogBuilder().Msg($"{separationLines}{McColorCodes.GREEN}Click here to finish the payment\n{separationLines}", info.DirctLink, "open link"));
        }

        private static void AddOptionsFor(MinecraftSocket socket, string letter, DialogBuilder db, List<TopUpProduct> topups)
        {
            var options = new int[] { 1800, 5400, 10800 };
            foreach (var item in options)
            {
                var matching = topups.Where(t => t.Slug == $"{letter}_cc_{item}").FirstOrDefault();
                if (matching == null)
                    continue;
                db.CoflCommand<TopUpCommand>(" " + socket.FormatPrice(item), matching.Slug, $"Topup {socket.FormatPrice(item)} coins via {matching.ProviderSlug} for {matching.Price} {matching.CurrencyCode}");
            }
        }
    }
}