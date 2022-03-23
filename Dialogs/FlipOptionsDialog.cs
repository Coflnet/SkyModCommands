using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class FlipOptionsDialog : Dialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            var flip = context.GetFlip();
            var redX = McColorCodes.DARK_RED + "✖" + McColorCodes.GRAY;
            var greenHeard = McColorCodes.DARK_GREEN + "❤" + McColorCodes.GRAY;
            var timingMessage = $"{McColorCodes.WHITE} ⌛{McColorCodes.GRAY}   Get own timings";
            return New().MsgLine("What do you want to do?")
                .CoflCommand<RateCommand>(
                    $" {redX}  downvote & report", 
                    $"{flip.Auction.Uuid} {flip.Finder} down", 
                    "Vote this flip down").Break
                .CoflCommand<RateCommand>(
                    $" {greenHeard}  upvote flip", 
                    $"{flip.Auction.Uuid} {flip.Finder} up", 
                    "Vote this flip up").Break
                .CoflCommand<TimeCommand>(
                    timingMessage, 
                    $"{flip.Auction.Uuid}", 
                    "Get your timings for flip").Break
                .CoflCommand<ReferenceCommand>(
                    $"{McColorCodes.WHITE}[?]{McColorCodes.GRAY} Get references", 
                    $"{flip.Auction.Uuid}", 
                    "Find out why this was deemed a flip").Break
                .CoflCommand<ReferenceCommand>(
                    " ➹  Open on website", 
                    $"https://sky.coflnet.com/a/{flip.Auction.Uuid}", 
                    "Open link").Break;

        }
    }
}
