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
        if (BazaarOrderStateHelper.IsOrderOptionsSnapshot(arguments))
        {
            const string message = "Wrong bazaar order state uploaded: received a single-order options window instead of the full bazaar order overview. Keeping previous bazaar orders.";
            Activity.Current?.Log(message);
            socket.Dialog(db => db.MsgLine(message));
            return Task.CompletedTask;
        }

        socket.SessionInfo.BazaarOrders = BazaarOrderStateHelper.ParseOpenOrders(arguments, parser);
        BazaarOrderStateHelper.SyncSentOrdersWithUpload(socket.SessionInfo.SentBazaarOrders, socket.SessionInfo.BazaarOrders);
        Activity.Current?.Log(JsonConvert.SerializeObject(socket.SessionInfo.BazaarOrders));
        Activity.Current?.Log("Bazaar orders tracked: " + socket.SessionInfo.ActiveBazaarOrderCount);
        if(socket.SessionInfo.ActiveBazaarOrderCount >= 1)
        {
            Activity.Current?.AddTag("orders", "some");
        }
        return Task.CompletedTask;
    }
}