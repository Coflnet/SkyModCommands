using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private const int MinimumUsCoinGateAmount = 5400;
        private static readonly string[] CoinGateCountries =
        [
            "AT", "BE", "BG", "BM", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GG",
            "GI", "GL", "GR", "GS", "HK", "HR", "HU", "IE", "IT", "JE", "KI", "LT", "LU",
            "LV", "MO", "MT", "MV", "NL", "PL", "PT", "RO", "SE", "SI", "SJ", "SK", "US"
        ];

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var productApi = socket.GetService<ProductsApi>();
            var topUpApi = socket.GetService<TopUpApi>();
            var userApi = socket.GetService<UserApi>();

            var input = arguments.Trim('"');
            if (string.IsNullOrEmpty(input))
            {
                var db = DialogBuilder.New;
                var topups = await productApi.ProductsTopupGetAsync(0, 100);
                db.MsgLine(McColorCodes.BLUE + "Topup using paypal directly - only for some of US and EU");
                AddOptionsFor(socket, "p", db, topups);
                db.Break.MsgLine(Indantation + McColorCodes.DARK_GREEN + "Topup using stripe - only for some of US and EU");
                AddOptionsFor(socket, "s", db, topups);
                db.Break.MsgLine(Indantation + McColorCodes.YELLOW + "Topup using crypto - only for some of US and EU");
                AddOptionsFor(socket, "c", db, topups);
                db.Break.MsgLine(Indantation + McColorCodes.GOLD + "Topup using lemonsqueezy (all around the globe)");
                AddOptionsFor(socket, "l", db, topups);
                socket.SendMessage(db);
                return;
            }

            var parts = input.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            var toBuy = parts[0];
            if (toBuy.StartsWith('c') && parts.Length == 1)
            {
                ShowCoinGateCountrySelection(socket, toBuy);
                return;
            }

            socket.SendMessage(new DialogBuilder().Msg($"Contacting payment provider", null, "Can take a few seconds"));

            var clientIp = socket.ClientIp;
            Activity.Current?.SetTag("clientIp", clientIp);
            if (string.IsNullOrWhiteSpace(clientIp))
                throw new CoflnetException("ip_not_found", "Your IP address could not be determined. Please reconnect and try again.");

            var accountInfo = socket.sessionLifesycle.AccountInfo.Value;
            var options = new TopUpOptions()
            {
                Locale = accountInfo.Locale,
                UserEmail = UserService.Instance.GetUserById(int.Parse(accountInfo.UserId)).Email,
                UserIp = clientIp,
                Country = toBuy.StartsWith('c') ? parts.ElementAtOrDefault(1)?.ToUpperInvariant() : null
            };
            TopUpIdResponse info;
            if (toBuy.StartsWith('s'))
                info = await topUpApi.TopUpStripePostAsync(socket.UserId, toBuy, options);
            else if (toBuy.StartsWith('p'))
                info = await topUpApi.TopUpPaypalPostAsync(socket.UserId, toBuy, options);
            else if (toBuy.StartsWith('l'))
                info = await topUpApi.TopUpLemonsqueezyPostAsync(socket.UserId, toBuy, options);
            else if (toBuy.StartsWith('c'))
                info = await topUpApi.TopUpCoingatePostAsync(socket.UserId, toBuy, options);
            else
                throw new CoflnetException("invalid_product", $"The product {toBuy} isn't know, please execute the command without arguments to get options");
            var separationLines = "--------------------\n";
            socket.SendMessage(new DialogBuilder().Msg($"{separationLines}{McColorCodes.GREEN}Click here to finish the payment\n{separationLines}", info.DirectLink, "open link"));
        }

        private static void ShowCoinGateCountrySelection(MinecraftSocket socket, string productId)
        {
            var coinAmount = int.TryParse(productId.Split('_').LastOrDefault(), out var amount) ? amount : 0;
            var db = DialogBuilder.New
                .MsgLine("Select your country for this crypto payment.")
                .MsgLine("Your selection must match the country of your current IP address.")
                .If(() => coinAmount < MinimumUsCoinGateAmount, db => db.MsgLine(
                    $"{McColorCodes.YELLOW}United States crypto payments require at least {socket.FormatPrice(MinimumUsCoinGateAmount)} CoflCoins."));

            foreach (var code in CoinGateCountries.Where(code => code != "US" || coinAmount >= MinimumUsCoinGateAmount)
                         .OrderBy(code => new RegionInfo(code).EnglishName))
            {
                var name = new RegionInfo(code).EnglishName;
                db.CoflCommandButton<TopUpCommand>(name, $"{productId} {code}", $"Select {name} ({code})")
                    .Msg(" ");
            }
            socket.SendMessage(db);
        }

        private static void AddOptionsFor(MinecraftSocket socket, string letter, DialogBuilder db, List<TopUpProduct> topups)
        {
            var options = new int[] { 1800, 5400, 10800, 21600 };
            db.Msg(Indantation);
            foreach (var item in options)
            {
                var matching = topups.Where(t => t.Slug == $"{letter}_cc_{item}").FirstOrDefault();
                if (matching == null)
                    continue;
                var postfix = "";
                if (item == 21600)
                    postfix += McColorCodes.GRAY + " (100 days prem+)";
                db.CoflCommand<TopUpCommand>($" {McColorCodes.DARK_GRAY}->{McColorCodes.WHITE}" + socket.FormatPrice(item) + postfix, matching.Slug,
                    $"Topup {McColorCodes.AQUA}{socket.FormatPrice(item)}{McColorCodes.GRAY} coins via {McColorCodes.AQUA}{matching.ProviderSlug}{McColorCodes.GRAY} for {McColorCodes.AQUA}{matching.Price} {matching.CurrencyCode}");
            }
        }
    }
}
