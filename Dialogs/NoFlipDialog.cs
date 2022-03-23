using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class NoBestFlipDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            return New()
                .Msg("No best flip available keep holding to open next")
            ;
        }
    } 
}
