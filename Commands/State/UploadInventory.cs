using System;
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
        if (!string.IsNullOrEmpty(arguments) && arguments.IndexOf("part", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var idx = arguments.IndexOf("part", StringComparison.OrdinalIgnoreCase);
            var start = Math.Max(0, idx - 2000);
            var end = Math.Min(arguments.Length, idx + "part".Length + 2000);
            var snippet = arguments.Substring(start, end - start);
            Activity.Current?.Log(snippet, 8500).AddTag("part","any");
        }
        else
        {
            Activity.Current?.Log(JsonConvert.SerializeObject(socket.SessionInfo.Inventory));
        }
        if (socket.ModAdapter is AfVersionAdapter)
        {
            return;
        }
        socket.AccountInfo.Tricks.TickFound("inventory");
        await socket.sessionLifesycle.AccountInfo.Update();
    }
}