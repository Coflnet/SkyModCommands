using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Services
{
    public class DialogService
    {
        private ClassNameDictonary<Dialog> Dialogs = new ClassNameDictonary<Dialog>();

        public DialogService()
        {
            Dialogs.Add<EchoDialog>();
            Dialogs.Add<OverpricedDialog>();
            Dialogs.Add<ReferencesWrongDialog>();
            Dialogs.Add<SlowSellDialog>();
            Dialogs.Add<ChatReportDialog>();
            Dialogs.Add<NoBestFlipDialog>();
            Dialogs.Add<FlipOptionsDialog>();
        }
        public ChatPart[] GetResponse(MinecraftSocket socket, string context)
        {
            var commandName = context.Split(' ').Where(s => !string.IsNullOrEmpty(s)).First();
            if (!Dialogs.TryGetValue(commandName.ToLower(), out Dialog instance))
                return new ChatPart[] { new ChatPart($"could not find a response {commandName}, sorry\n if you need help please raise a bug report on the discord") };
            return instance.GetResponse(new DialogArgs()
            {
                Context = context.Replace(commandName, "").Trim(),
                socket = socket
            });
        }

    }
}
