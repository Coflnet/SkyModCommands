using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class UploadBazaarOrders : McCommand
{
    private readonly InventoryParser parser = new();

    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.SessionInfo.BazaarOrders = BazaarOrderStateHelper.ParseOpenOrders(arguments, parser);
        Activity.Current?.Log(JsonConvert.SerializeObject(socket.SessionInfo.BazaarOrders));
        Activity.Current?.Log("Bazaar orders tracked: " + socket.SessionInfo.ActiveBazaarOrderCount);
        return Task.CompletedTask;
    }
}