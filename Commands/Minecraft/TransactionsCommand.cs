using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Past /cofl buy transactions", 
        "A list of transactions of CoflCoins",
        "Allows you to check where they came from and went to")]
    public class TransactionsCommand : ReadOnlyListCommand<ExternalTransaction>
    {
        public override bool IsPublic => true;
        protected override void Format(MinecraftSocket socket, DialogBuilder db, ExternalTransaction elem)
        {
            if(elem.Amount < 0)
            {
                var product = elem.ProductId;
                if(product.StartsWith("transfer"))
                {
                    product = "transfer";
                    db.MsgLine($"{McColorCodes.YELLOW}Transferred {McColorCodes.RED}{elem.Amount}{McColorCodes.YELLOW} to another user on {McColorCodes.AQUA}{elem.TimeStamp}{McColorCodes.YELLOW}");
                    return;
                }
                if(product.StartsWith("premium_plus"))
                    product = "prem+";
                else if(product.StartsWith("premium"))
                    product = "premium";
                else if(product.StartsWith("pre_api"))
                    product = $"{McColorCodes.RED}pre_api";
                else if(product.StartsWith("starter"))
                    product = "starter";
                else if(product.StartsWith("test-premium"))
                    product = "test-premium";
                db.MsgLine($"{McColorCodes.YELLOW}Purchased {McColorCodes.AQUA}{product}{McColorCodes.YELLOW} on {McColorCodes.AQUA}{elem.TimeStamp}{McColorCodes.YELLOW} for {McColorCodes.RED}{elem.Amount} ");
            }
            else if(elem.ProductId == "compensation")
                db.MsgLine($"{McColorCodes.GREEN}Received {elem.ProductId} {McColorCodes.GREEN}{elem.Amount} {McColorCodes.YELLOW}on {McColorCodes.AQUA}{elem.TimeStamp}{McColorCodes.YELLOW} for {elem.Reference}");
            else if (elem.ProductId == "transfer")
                db.MsgLine($"{McColorCodes.GREEN}Received {elem.ProductId}{McColorCodes.YELLOW} on {McColorCodes.AQUA}{elem.TimeStamp}{McColorCodes.YELLOW} for {McColorCodes.GREEN}{elem.Amount} ");
            else if (elem.ProductId == "verify-mc")
                db.MsgLine($"{McColorCodes.GREEN}Received {McColorCodes.GREEN}{elem.Amount}{McColorCodes.YELLOW} on {McColorCodes.AQUA}{elem.TimeStamp}{McColorCodes.YELLOW} for verifying minecraft account");
            else
                db.MsgLine($"{McColorCodes.GREEN}Bought {McColorCodes.GREEN}{elem.Amount} CoflCoins{McColorCodes.YELLOW} on {McColorCodes.AQUA}{elem.TimeStamp}{McColorCodes.YELLOW} via {(elem.ProductId.StartsWith("p") ? "Paypal" : "Stripe")}", null, elem.ProductId);
            //db.MsgLine($"{McColorCodes.YELLOW}Purchased {McColorCodes.AQUA}{elem.ProductId}{McColorCodes.YELLOW} on {McColorCodes.YELLOW}{elem.TimeStamp}{McColorCodes.YELLOW} for {McColorCodes.AQUA}{elem.Amount} ");
        }

        protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<ExternalTransaction> elements)
        {
            var spent = elements.Where(e => e.Amount < 0).Sum(e => e.Amount) * -1;
            var inRealMoney = (long)((spent - elements.Where(e => e.ProductId == "compensation").Sum(e => e.Amount)) / 1800 * 6.69);
            var preApiHourCount = elements.Count(e => e.ProductId.StartsWith("pre_api"));
            db.Msg($"{McColorCodes.YELLOW}Total coins spent: {McColorCodes.AQUA}{socket.formatProvider.FormatPrice((long)spent)} {McColorCodes.YELLOW}Pre api hours: {McColorCodes.AQUA}{preApiHourCount}",
                   null,
                   $"{McColorCodes.YELLOW}Aproximate total spent: {McColorCodes.AQUA}{socket.formatProvider.FormatPrice(inRealMoney)}€ \n{McColorCodes.YELLOW}excluding compensations");
        }

        protected override async Task<IEnumerable<ExternalTransaction>> GetElements(MinecraftSocket socket, string val)
        {
            var userApi = socket.GetService<ITransactionApi>();
            return (await userApi.TransactionUUserIdGetAsync(socket.UserId)).OrderByDescending(e => e.TimeStamp);
        }

        protected override string GetId(ExternalTransaction elem)
        {
            return elem.TimeStamp.ToString();
        }
    }
}