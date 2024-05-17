using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class UploadUpperInventory : McCommand
{
    private InventoryParser parser = new InventoryParser();
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        var parsed = parser.Parse(arguments).ToList();
        Activity.Current?.Log(JsonConvert.SerializeObject(parsed));
        socket.Send(new Response("inventoryReceivedDebug", JsonConvert.SerializeObject(parsed.Where(i => i != null && i.Tag != null).FirstOrDefault())));
        return Task.CompletedTask;
    }
}