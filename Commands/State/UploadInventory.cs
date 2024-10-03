using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class UploadInventory : McCommand
{
    private InventoryParser parser = new InventoryParser();
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.SessionInfo.Inventory = null;
        socket.SessionInfo.Inventory = parser.Parse(arguments).ToList();
        Activity.Current?.Log(JsonConvert.SerializeObject(socket.SessionInfo.Inventory));
        if (socket.ModAdapter is AfVersionAdapter)
        {
            return;
        }
        socket.AccountInfo.Tricks.TickFound("inventory");
        await socket.sessionLifesycle.AccountInfo.Update();
    }
}