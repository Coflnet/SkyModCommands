using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class UploadBazaarOrders : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        Activity.Current.Log("Bazaar orders updated " + arguments);
        socket.Dialog(db => db.MsgLine("Bazaar orders updated", null, "This can be used to trigger bazaar order related tasks"));
    }
}