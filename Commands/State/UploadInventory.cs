using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;
public class UploadInventory : McCommand
{
    InventoryParser parser = new InventoryParser();
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.SessionInfo.Inventory = parser.Parse(arguments).ToList();
        socket.SendMessage("Inventory uploaded " + socket.SessionInfo.Inventory.Count + " items");
        // does nothing for now
        return Task.CompletedTask;
    }
}