using System;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
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
                var timeString = GetLenghtInWords(product, count);

                socket.SendMessage(new DialogBuilder()
                        .Msg($"Do you want to buy the {McColorCodes.AQUA}{product.Title}{McColorCodes.WHITE} service {McColorCodes.AQUA}{count}x ")
                        .Msg($"for a total of {McColorCodes.AQUA}{socket.FormatPrice((long)product.Cost)}{McColorCodes.WHITE}{McColorCodes.ITALIC} cofl coins ")
                        .MsgLine($"lasting {timeString}")
                        .CoflCommand<PurchaseCommand>(
                                $"  {McColorCodes.GREEN}Yes  ",
                                $"{productSlug} {count} {socket.SessionInfo.ConnectionId}",
                                $"Confirm purchase (paying {socket.FormatPrice((long)product.Cost)} cofl coins)")
                        .DialogLink<EchoDialog>($"  {McColorCodes.RED}No  ", $"Purchase Canceled", $"{McColorCodes.RED}Cancel purchase"));
                return;
            }

            var targetConId = parts[2];
            if (targetConId != socket.SessionInfo.ConnectionId)
                throw new Coflnet.Sky.Core.CoflnetException("no_conid_match", "The purchase was started on a different connection. To prevent loss of coins please start over again.");

            try
            {
                var userInfo = await userApi.UserUserIdServicePurchaseProductSlugPostAsync(socket.UserId, productSlug, socket.SessionInfo.ConnectionId, count);
                socket.Dialog(db => db.MsgLine($"Successfully started purchase of {productSlug} you should receive a confirmation in a few seconds"));
            }
            catch (Coflnet.Payments.Client.Client.ApiException e)
            {
                socket.SendMessage(DialogBuilder.New.MsgLine(McColorCodes.RED + "An error occured").Msg(e.Message.Substring(68).Trim('}', '"')));
            }

        }

        private static string GetLenghtInWords(PurchaseableProduct product, int count)
        {
            var timeSpan = TimeSpan.FromSeconds(product.OwnershipSeconds * count);
            var timeString = $"{McColorCodes.AQUA}{(int)timeSpan.TotalDays} days";
            if (timeSpan < TimeSpan.FromDays(1))
                timeString = $"{McColorCodes.AQUA}{(int)timeSpan.TotalHours} hour" + (timeSpan == TimeSpan.FromHours(1) ? "" : "s");
            return timeString;
        }
    }
}