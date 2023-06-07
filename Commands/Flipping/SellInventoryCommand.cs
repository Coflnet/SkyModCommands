using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;

public class SellInventoryCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if(socket.ModAdapter is not AfVersionAdapter)
            throw new CoflnetException("forbidden", "This command is only available with an autoflipper client");
        socket.SessionInfo.SellAll = true;
        foreach (var item in socket.SessionInfo.Inventory.Skip(9).Where(i => i != null))
        {
            socket.Dialog(db => db.MsgLine($"§7[§6§lSelling§7]§r{item.ItemName}"));
        }
        socket.Dialog(db => db.Msg("Starting to sell all items in your inventory (except armor). \nPlease make sure there is nothing in your inventory you don't want to sell (see above for list)."));
    }
}
