using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class DerpyCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.Dialog(db => db.MsgLine("Hello there, this command allows you to extend your premium for 5 days with your derpy compensation.")
                .CoflCommand<PurchaseCommand>(McColorCodes.AQUA + "start purchase", "premium-derpy", "Shows you a sumary and confrimation screen"));
            return Task.CompletedTask;
        }
    }
}
