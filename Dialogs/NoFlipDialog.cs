using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class NoBestFlipDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            var shouldBeHidden = context.socket.Settings?.ModSettings?.HideNoBestFlip ?? false;
            if(shouldBeHidden)
                return null;
            return New()
                .Msg("No best flip available keep holding to open next")
            ;
        }
    } 
}
