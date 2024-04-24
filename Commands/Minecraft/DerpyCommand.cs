using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class DerpyCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.Dialog(db => db.MsgLine("Hello there, you don't need to buy premium anymore. It gets extended for as long as derpy is/was mayor.")
                .CoflCommand<PurchaseCommand>(McColorCodes.AQUA + "Still buy 5 days of premium", "premium-derpy", "Shows you a sumary and confrimation screen"));
            return Task.CompletedTask;
        }
    }
}
