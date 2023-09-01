using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

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
                response = response.CoflCommand<WhichBLEntryCommand>(McColorCodes.GREEN + "matched your whitelist, click to see which",
                         JsonConvert.SerializeObject(new WhichBLEntryCommand.Args() { Uuid = flip.Auction.Uuid, WL = true })).Break;

            response = AddBlockedReason(context, flip, response);

            response = response.CoflCommand<RateCommand>(
                $" {redX}  downvote / report",
                $"{flip.Auction.Uuid} {flip.Finder} down",
                "Vote this flip down").Break
            .CoflCommand<RateCommand>(
                $" {greenHeard}  upvote flip",
                $"{flip.Auction.Uuid} {flip.Finder} up",
                "Vote this flip up").Break
            .CoflCommand<BlacklistCommand>(
                $" {redX}  Blacklist this item",
                $"add {flip.Auction.Tag} forceBlacklist=true",
                $"Don't show {McColorCodes.AQUA}{ItemReferences.RemoveReforgesAndLevel(flip.Auction.ItemName)}{McColorCodes.RED} AT ALL anymore")
            .CoflCommand<BlacklistCommand>(
                $" {McColorCodes.GREEN}for 1week,",
                $"add {flip.Auction.Tag} forceBlacklist=true removeAfter={DateTime.UtcNow.AddDays(7).ToString("s")}",
                $"Don't show {McColorCodes.AQUA}{ItemReferences.RemoveReforgesAndLevel(flip.Auction.ItemName)}{McColorCodes.GREEN} for a week")
            .CoflCommand<BlacklistCommand>(
                $" {McColorCodes.GREEN}{McColorCodes.ITALIC}1 day",
                $"add {flip.Auction.Tag} forceBlacklist=true removeAfter={DateTime.UtcNow.AddDays(1).ToString("s")}",
                $"Don't show {McColorCodes.AQUA}{ItemReferences.RemoveReforgesAndLevel(flip.Auction.ItemName)}{McColorCodes.GREEN} for 24 hours")
            .CoflCommand<BlacklistCommand>(
                $" {McColorCodes.YELLOW}seller",
                $"add seller={flip.Auction.AuctioneerId} forceBlacklist=true",
                $"Don't show seller {McColorCodes.AQUA}{flip.Auction.AuctioneerId}{McColorCodes.YELLOW} AT ALL anymore")
                .Break
            .CoflCommand<TimeCommand>(
                timingMessage,
                $"{flip.Auction.Uuid}",
                "Get your timings for flip").Break
            .CoflCommand<AhOpenCommand>(
                $"{McColorCodes.GOLD} AH {McColorCodes.GRAY}open seller's ah ",
                $"{flip.Auction.AuctioneerId}",
                "Open the sellers ah")
                .CoflCommand<GetMcNameForCommand>(McColorCodes.DARK_GREEN + " Get Name", flip.Auction.AuctioneerId, "Get the name of the seller").Break
            .CoflCommand<ReferenceCommand>(
                $"{McColorCodes.WHITE}[?]{McColorCodes.GRAY} Get references",
                $"{flip.Auction.Uuid}",
                "Find out why this was deemed a flip").Break
                .MsgLine(
                    " ➹  Open on website",
                    $"https://sky.coflnet.com/a/{flip.Auction.Uuid}",
                    "Open link");

            if (context.socket.GetService<Services.ModeratorService>().IsModerator(context.socket))
                response.Msg(McColorCodes.DARK_GRAY + " . ", null, flip.Auction.Context.GetValueOrDefault("pre-api"));
            return response;
        }

        private static DialogBuilder AddBlockedReason(DialogArgs context, LowPricedAuction flip, DialogBuilder response)
        {
            var flipInstance = FlipperService.LowPriceToFlip(flip);
            var passed = context.socket.Settings.MatchesSettings(flipInstance);
            if (!passed.Item1)
                if (flip.AdditionalProps.TryGetValue("match", out string bldetails) && bldetails.Contains("blacklist"))
                    response = response.CoflCommand<WhichBLEntryCommand>(McColorCodes.RED + "matched your blacklist, click to see which",
                             JsonConvert.SerializeObject(new WhichBLEntryCommand.Args() { Uuid = flip.Auction.Uuid })).Break;
                else
                    if (passed.Item2 == "profit Percentage")
                    response = response.MsgLine($"{McColorCodes.RED} Blocked because of {passed.Item2} - {context.FormatNumber(flipInstance.ProfitPercentage)}%");
                else
                    response = response.MsgLine($"{McColorCodes.RED} Blocked because of {passed.Item2}");
            return response;
        }
    }
}
