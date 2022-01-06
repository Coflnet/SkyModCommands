using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class TimeCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var flip =  socket.LastSent.LastOrDefault();
            Dictionary<string, string> context = flip?.Auction.Context;
            var msg = new DialogBuilder()
                .MsgLine("These are the relatives times to the api update")
                .AddTime(context, "FindTime", "fT")
                .AddTime(context, "Flipper Receive", "frec")
                .AddTime(context, "Flipper Send", "fsend")
                .AddTime(context, "Command Receive", "crec")
                .AddTime(flip.AdditionalProps, "Command Send", "csend")
                .AddTime(flip.AdditionalProps, "Click ", "clickT")
            ;
            socket.SendMessage(msg.Build());
        }

        
    }

    public static class TimeCommandOverloads
    {
        public static DialogBuilder AddTime(this DialogBuilder msg, Dictionary<string, string> context,  string label, string key)
        {
            return msg.MsgLine($"{McColorCodes.GRAY}{label}: {McColorCodes.WHITE}" + context.GetValueOrDefault(key));
        }
    }
}