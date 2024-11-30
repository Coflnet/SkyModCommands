using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{

    [CommandDescription("Start purchase of a paid plan",
        "To buy a plan use /cofl buy <plan> [count]",
        "Allows you to buy premium and other plans",
        "Buy premium to support the server <3",
        "Example /cofl buy premium+ 3")]
    public class PurchaseCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var productApi = socket.GetService<ProductsApi>();
            var topUpApi = socket.GetService<TopUpApi>();
            var userApi = socket.GetService<UserApi>();

            var parts = arguments.Trim('"').Split(' ');
            var productSlug = parts[0];
            if (productSlug == "prem+" || productSlug == "premium+")
                productSlug = "premium_plus";

            if (string.IsNullOrEmpty(productSlug))
            {
                socket.Dialog(db => db.MsgLine($"What plan do you want to purchase/extend {McColorCodes.GRAY}(click on it)")
                        .CoflCommand<PurchaseCommand>($" {McColorCodes.GOLD}premium+ (week)", "premium_plus 1", $"Purchase {McColorCodes.GOLD}prem+")
                        .CoflCommand<PurchaseCommand>($" {McColorCodes.GREEN}premium (month)", "premium 1", $"purchase {McColorCodes.GREEN}premium")
                        .CoflCommand<PurchaseCommand>($" {McColorCodes.WHITE}starter (180 days)", "starter_premium 1", $"purchase starter premium for {McColorCodes.AQUA}half a year").LineBreak()
                        .CoflCommand<PurchaseCommand>($" {McColorCodes.GOLD}premium+ (hour)", "premium_plus-hour 1", $"Purchase {McColorCodes.GOLD}prem+{McColorCodes.WHITE} for 60 minutes")
                        .CoflCommand<PurchaseCommand>($" {McColorCodes.GREEN}premium(derpy)", "premium-derpy 1", $"purchase {McColorCodes.GREEN}premium{McColorCodes.WHITE} for the time derpy was mayor (5 days)")
                        .CoflCommand<PurchaseCommand>($" {McColorCodes.WHITE}starter (a day)", "starter_premium-day 1", $"purchase starter premium for a {McColorCodes.AQUA}single day").LineBreak());
                return;
            }

            Product product = null;
            RuleResult adjustedProduct = null;
            try
            {
                adjustedProduct = await userApi.UserUserIdPriceForProductSlugGetAsync(socket.UserId.ToString(), productSlug);
                product = adjustedProduct.ModifiedProduct;
                if (product == null)
                {
                    socket.SendMessage(new DialogBuilder().MsgLine($"The product {productSlug} could not be fund"));
                    return;
                }
            }
            catch (Payments.Client.Client.ApiException e)
            {
                socket.SendMessage(new DialogBuilder().MsgLine(e.Message.Substring("Error calling UserUserIdPriceForProductSlugGet: {\"Message\":\"".Length).TrimEnd('"', '}')));
                return;
            }


            var count = 1;
            if (parts.Length > 1)
                if (!int.TryParse(parts[1], out count))
                {
                    socket.SendMessage(new DialogBuilder().MsgLine($"The count argument after {productSlug} has to be a number from 1-12"));
                    return;
                }
            if (parts.Length < 4 || parts[2] != socket.SessionInfo.ConnectionId)
            {
                var timeString = GetLenghtInWords(product, count);
                var costSum = socket.FormatPrice((long)product.Cost * count);

                socket.Dialog(db => db
                        .Msg($"Do you want to buy the {McColorCodes.AQUA}{product.Title}{McColorCodes.WHITE} service {McColorCodes.AQUA}{count}x ", null, product.Description)
                        .Msg($"for a total of {McColorCodes.AQUA}{costSum}{McColorCodes.WHITE}{McColorCodes.ITALIC} cofl coins ")
                        .MsgLine($"lasting {timeString}")
                        .CoflCommand<PurchaseCommand>(
                                $"  {McColorCodes.GREEN}Yes  ",
                                $"{productSlug} {count} {socket.SessionInfo.ConnectionId} {DateTime.UtcNow:hh:mm}",
                                $"Confirm purchase (paying {costSum} cofl coins)")
                        .DialogLink<EchoDialog>($"  {McColorCodes.RED}No  ", $"Purchase Canceled", $"{McColorCodes.RED}Cancel purchase"));
                if (adjustedProduct.Rules.Count == 0)
                    return;
                var rule = adjustedProduct.Rules.First();
                var amount = rule.Amount;
                var append = rule.Flags.Value.HasFlag(RuleFlags.PERCENT) ? "%" : " CoflCoins";
                socket.Dialog(db => db.MsgLine($"Price was adjusted by {McColorCodes.AQUA}{amount + append}{McColorCodes.RESET} because you own {McColorCodes.GOLD}{adjustedProduct.Rules.First().Requires.Slug}"));
                return;
            }

            var targetConId = parts[2];
            if (targetConId != socket.SessionInfo.ConnectionId)
                throw new Core.CoflnetException("no_conid_match", "The purchase was started on a different connection. To prevent loss of coins please start again.");
            await Purchase(socket, userApi, productSlug, count, targetConId + parts[3]);
        }

        public static async Task<bool> Purchase(IMinecraftSocket socket, IUserApi userApi, string productSlug, int count, string reference = null)
        {
            try
            {
                if (reference == null)
                    reference = socket.SessionInfo.ConnectionId.Substring(0, 10) + DateTime.UtcNow.ToString("hh:mm");
                await userApi.UserUserIdServicePurchaseProductSlugPostAsync(socket.UserId, productSlug, reference, count);
                socket.Dialog(db => db.MsgLine($"Successfully started purchase of {productSlug} you should receive a confirmation in a few seconds"));
                await Task.Delay(TimeSpan.FromSeconds(2));
                await socket.sessionLifesycle.TierManager.RefreshTier();
                socket.sessionLifesycle.UpdateConnectionTier(await socket.sessionLifesycle.TierManager.GetCurrentCached());
                return true;
            }
            catch (Payments.Client.Client.ApiException e)
            {
                var message = e.Message.Substring(68).Trim('}', '"');
                socket.SendMessage(DialogBuilder.New.MsgLine(McColorCodes.RED + "An error occured").Msg(message)
                    .If(() => e.Message.Contains("insuficcient balance"), db => db.CoflCommand<TopUpCommand>(McColorCodes.AQUA + "Click here to top up coins", "", "Click here to buy coins"))
                    .If(() => e.Message.Contains("same reference found"), db =>
                        db.MsgLine(
                            McColorCodes.AQUA + "To prevent accidental loss of coins you can only purchase once a minute.", null,
                            "You can buy multiple at once by adding a number after the product name\n"
                               + $"Example: {McColorCodes.AQUA}/cofl buy pre_api 3")));
            }
            return false;
        }

        private static string GetLenghtInWords(Product product, int count)
        {
            var timeSpan = TimeSpan.FromSeconds(product.OwnershipSeconds * count);
            var timeString = $"{McColorCodes.AQUA}{(int)timeSpan.TotalDays} days";
            if (timeSpan < TimeSpan.FromDays(1))
                timeString = $"{McColorCodes.AQUA}{(int)timeSpan.TotalHours} hour" + (timeSpan == TimeSpan.FromHours(1) ? "" : "s");
            return timeString;
        }
    }
}