using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Shows you the best item to convert your bits for coins")]
public class BitsCommand : ReadOnlyListCommand<BitService.Option>
{
    protected override string Title => "Bit Options";
    protected override int PageSize => 10;

    protected override async Task<IEnumerable<BitService.Option>> GetElements(MinecraftSocket socket, string val)
    {
        var bitService = socket.GetService<BitService>();
        return (await bitService.GetOptions()).OrderByDescending(o => o.CoinsPerBit);
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, BitService.Option elem)
    {
        db.MsgLine($"{McColorCodes.YELLOW}{elem.Name} {McColorCodes.GOLD}{socket.FormatPrice(elem.CoinsPerBit)} coins per bit",
            "/warp elizabeth",
            $"\n{McColorCodes.GRAY}Sell price: {McColorCodes.YELLOW}~{socket.FormatPrice(elem.CoinsPerBit * elem.TotalBits)} coins\n"
          + $"{McColorCodes.GRAY}Total Bits required: {McColorCodes.YELLOW}{elem.TotalBits}");
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<BitService.Option> elements, IEnumerable<BitService.Option> toDisplay)
    {
        var available = socket.SessionInfo.Bits;
        if (available <= 0)
            return;
        var bestAffordable = elements.Where(e => e.TotalBits <= available).OrderByDescending(e => e.CoinsPerBit).FirstOrDefault();
        if (bestAffordable == null)
            return;
        db.MsgLine($"Best affordable option is {McColorCodes.YELLOW}{bestAffordable.Name}{McColorCodes.GRAY} for {McColorCodes.YELLOW}{socket.FormatPrice(bestAffordable.CoinsPerBit * bestAffordable.TotalBits)} coins",
        "/warp elizabeth", $"{McColorCodes.YELLOW}Click to warp to Community Shop");
    }

    protected override string GetId(BitService.Option elem)
    {
        return elem.Tag + elem.Name;
    }
}
