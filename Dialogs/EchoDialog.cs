using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class EchoDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            return New().Msg(context.Context);
        }
    }
}
