using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class EchoDialog : Dialog
    {
        public override ChatPart[] GetResponse(string context)
        {
            return New().Msg(context);
        }
    }
}
