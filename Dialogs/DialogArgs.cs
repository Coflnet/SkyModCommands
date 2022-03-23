using System.Linq;
using Coflnet.Sky.Commands.MC;
using hypixel;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class DialogArgs
    {
        public string Context;
        public MinecraftSocket socket;

        public LowPricedAuction GetFlip()
        {
            return socket.GetFlip(Context.Split(' ').Where(w=>w.Length == 32).FirstOrDefault()) 
                ?? throw new CoflnetException("flip_not_found", "The requested flip could not be found on your session.");
        }
    }
}
