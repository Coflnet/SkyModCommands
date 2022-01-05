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
            Dictionary<string, string> context = socket.LastSent.LastOrDefault()?.Auction.Context;
            var msg = new DialogBuilder()
                .MsgLine("These are the relatives times to the api update")
                .AddTime(context, "FindTime", "fT")
                .AddTime(context, "Flipper Receive", "frec")
                .AddTime(context, "Flipper Send", "fsend")
                .AddTime(context, "Command Receive", "crec")
                .AddTime(context, "Command Send", "csend")
                .AddTime(context, "Click ", "clickT")
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