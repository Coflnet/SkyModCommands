using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Calculates the auction house tax for a given sell amount",
    "Honors fee changes of Derpy",
    "Usage: /cofl ahtax <sellAmount>")]
public class AhTaxCommand : ArgumentsCommand
{
    protected override string Usage => "<sellAmount>";
    public override bool IsPublic => true;
    protected override Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var sellAmount = NumberParser.Double(args["sellAmount"]);
        var feerate = FlipInstance.GetFeeRateForStartingBid((int)sellAmount);
        var total = sellAmount * feerate / 100;
        socket.Dialog(db => db.MsgLine($"At {McColorCodes.AQUA}{socket.FormatPrice(sellAmount)} {McColorCodes.RESET}ah tax is {McColorCodes.AQUA}{socket.FormatPrice(total)} {McColorCodes.GRAY}({feerate}%)."));
        return Task.CompletedTask;
    }
}