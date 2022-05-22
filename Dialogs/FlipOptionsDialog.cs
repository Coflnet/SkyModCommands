using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

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
            var response = New().MsgLine("What do you want to do?");
            if (flip.AdditionalProps.TryGetValue("match", out string details) && details.Contains("whitelist"))
                response = response.CoflCommand<WhichBLEntryCommand>("\n" + McColorCodes.GREEN + "matched your whitelist, click to see which",
                         JsonConvert.SerializeObject(new WhichBLEntryCommand.Args() { Uuid = flip.Auction.Uuid, WL = true }));

            var passed = context.socket.Settings.MatchesSettings(FlipperService.LowPriceToFlip(flip));
            if (!passed.Item1)
                if (flip.AdditionalProps.TryGetValue("match", out details) && details.Contains("blacklist"))
                    response = response.CoflCommand<WhichBLEntryCommand>(McColorCodes.RED + "matched your blacklist, click to see which",
                             JsonConvert.SerializeObject(new WhichBLEntryCommand.Args() { Uuid = flip.Auction.Uuid })).Break;
                else 
                    response = response.MsgLine($"{McColorCodes.RED} Blocked because of {passed.Item2}");

            response = response.CoflCommand<RateCommand>(
                $" {redX}  downvote / report",
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
            .CoflCommand<BlacklistCommand>(
                $" {redX}  Blacklist this item",
                $"add {flip.Auction.Tag}",
                $"Don't show this {McColorCodes.AQUA}{Sky.Core.ItemReferences.RemoveReforgesAndLevel(flip.Auction.ItemName)}{McColorCodes.GRAY} anymore").Break
            .MsgLine(
                " ➹  Open on website",
                $"https://sky.coflnet.com/a/{flip.Auction.Uuid}",
                "Open link").Break;
            return response;
        }
    }
}
