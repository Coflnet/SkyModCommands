using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class TimeCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var flip = socket.LastSent.LastOrDefault();
            if (arguments.Length > 30)
                flip = socket.GetFlip(arguments.Trim('"'));
            if (flip == null)
            {
                socket.SendMessage(new DialogBuilder().MsgLine("Can't get timings", null, "sorry :("));
                throw new System.Exception("Flip not found on connection");
            }
            Dictionary<string, string> context = flip?.Auction.Context;
            var msg = new DialogBuilder()
                .MsgLine("These are the relatives times to the api update")
                .AddTime(context, "FindTime", "fT", "When the auction was first found and parsed")
                .AddTime(context, "Flipper Receive", "frec", "When the flip finder algorithm received the auction")
                .AddTime(context, "Flipper Send", "fsend", "When the flip finder was done calulating and sent the auction queue")
                .AddTime(context, "Command Receive", "crec", "When the mod backend server received the flip", "Mod backend is called `Command` because it handles commands")
                .AddTime(context, "Schedule", "csh", "Flip was scheduled to be filtered", "(here is prem+ difference)")
                .AddTime(flip.AdditionalProps, "Filter", "da", "Filtering started")
                .AddTime(flip.AdditionalProps, "Delay", "dl", "Filtering was done, flip met filter", "and is waiting for fairness delay")
                .AddTime(flip.AdditionalProps, "Command Send", "csend", "Time when flip left the mod backend server")
                .AddTime(flip.AdditionalProps, "Click ", "clickT", "Estimated time when the flip message was clicked")
            ;
            socket.SendMessage(msg.Build());
            return Task.CompletedTask;
        }


    }

    public static class TimeCommandOverloads
    {
        public static DialogBuilder AddTime(this DialogBuilder msg, Dictionary<string, string> context, string label, string key, params string[] desc)
        {
            return msg.MsgLine($"{McColorCodes.GRAY}{label.PadRight(15)}: {McColorCodes.WHITE}" + context?.GetValueOrDefault(key, "§onotavailable§r"), null, string.Join("\n", desc));
        }
    }
}