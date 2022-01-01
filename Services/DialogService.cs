using System;
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
        }
        public ChatPart[] GetResponse(string context)
        {
            var commandName = context.Split(' ').First();
            if(!Dialogs.TryGetValue(commandName.ToLower(),out Dialog instance))
                return new ChatPart[]{new ChatPart("could not find a response to that, sorry\n if you need help please raise a bug report on the discord")};
            return instance.GetResponse(context.Replace(commandName,"").Trim());
        }
    }
}
