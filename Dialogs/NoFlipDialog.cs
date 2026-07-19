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
            var areFilsDisabled = context.socket.HasFlippingDisabled();
            if(areFilsDisabled)
            return New()
                .CoflCommand<BlockedCommand>(
                    "You currently don't have flips enabled, click this message to get more info.", "", 
                    $"{McColorCodes.YELLOW}shows why no flips are showing");
            return New()
                .Msg("No best flip available. Keep holding to open next");
        }
    } 
}
