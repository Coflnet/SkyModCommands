using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class UploadInventory : McCommand
{
    private InventoryParser parser = new InventoryParser();
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.SessionInfo.Inventory = null;
        socket.SessionInfo.Inventory = parser.Parse(arguments).ToList();
        Activity.Current?.Log(JsonConvert.SerializeObject(socket.SessionInfo.Inventory));
        // does nothing for now
        return Task.CompletedTask;
    }
}