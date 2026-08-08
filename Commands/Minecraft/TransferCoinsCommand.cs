using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;
public class TransferCoinsCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.SendMessage("Coins are not transfarable anymore. The feature only holds up against US law while lots of users are outside of the US so it was removed.");
    }
}
