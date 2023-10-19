using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class TopUpCommand : McCommand
    {
        private const string Indantation = "      ";
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
                db.MsgLine(McColorCodes.BLUE + "Topup using paypal directly - only for some of US and EU");
                AddOptionsFor(socket, "p", db, topups);
                db.Break.MsgLine(Indantation + McColorCodes.DARK_GREEN + "Topup using stripe - only for some of US and EU");
                AddOptionsFor(socket, "s", db, topups);
                db.Break.MsgLine(Indantation + McColorCodes.GOLD + "Topup using lemonsqueezy (all around the globe)");
                AddOptionsFor(socket, "l", db, topups);
                socket.SendMessage(db);
                return;
            }
            socket.SendMessage(new DialogBuilder().Msg($"Contacting payment provider", null, "Can take a few seconds"));

            TopUpIdResponse info = new();
            if(toBuy.StartsWith('s'))
                info = await topUpApi.TopUpStripePostAsync(socket.UserId, toBuy, new());
            else if(toBuy.StartsWith('p'))
                info = await topUpApi.TopUpPaypalPostAsync(socket.UserId, toBuy, new());
            else if(toBuy.StartsWith('l'))
                info = await topUpApi.TopUpLemonsqueezyPostAsync(socket.UserId, toBuy, new());
            else
                throw new CoflnetException("invalid_product", $"The product {toBuy} isn't know, please execute the command without arguments to get options");
            var separationLines = "--------------------\n";
            socket.SendMessage(new DialogBuilder().Msg($"{separationLines}{McColorCodes.GREEN}Click here to finish the payment\n{separationLines}", info.DirctLink, "open link"));
        }

        private static void AddOptionsFor(MinecraftSocket socket, string letter, DialogBuilder db, List<TopUpProduct> topups)
        {
            var options = new int[] { 1800, 5400, 10800 };
            db.Msg(Indantation);
            foreach (var item in options)
            {
                var matching = topups.Where(t => t.Slug == $"{letter}_cc_{item}").FirstOrDefault();
                if (matching == null)
                    continue;
                db.CoflCommand<TopUpCommand>($" {McColorCodes.DARK_GRAY}->{McColorCodes.WHITE}" + socket.FormatPrice(item), matching.Slug, 
                    $"Topup {McColorCodes.AQUA}{socket.FormatPrice(item)}{McColorCodes.GRAY} coins via {McColorCodes.AQUA}{matching.ProviderSlug}{McColorCodes.GRAY} for {McColorCodes.AQUA}{matching.Price} {matching.CurrencyCode}");
            }
        }
    }
}