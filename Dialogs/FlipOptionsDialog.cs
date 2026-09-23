using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class FlipOptionsDialog : OfferOptionsDialog
    {
        public override ChatPart[] GetResponse(DialogArgs context)
        {
            var flip = context.GetFlip();
            var redX = McColorCodes.DARK_RED + "✖" + McColorCodes.GRAY;
            var greenHeard = McColorCodes.DARK_GREEN + "❤" + McColorCodes.GRAY;
            var timingMessage = $"{McColorCodes.WHITE} ⌛{McColorCodes.GRAY}   Get own timings";
            var response = New().MsgLine("What do you want to do?");
            if (flip.AdditionalProps.TryGetValue("match", out string details) && details.Contains("whitelist"))
                response = response.CoflCommand<WhichBLEntryCommand>(McColorCodes.GREEN + McColorCodes.OBFUSCATED + " - " + McColorCodes.RESET + McColorCodes.GREEN + "matched your whitelist, click to see which",
                         JsonConvert.SerializeObject(new WhichBLEntryCommand.Args() { Uuid = flip.Auction.Uuid, WL = true })).Break;

            response = AddBlockedReason(context, flip, response);

            response = response.CoflCommand<RateCommand>(
                $" {redX}  downvote / report",
                $"{flip.Auction.Uuid} {flip.Finder} down",
                "Vote this flip down").Break
            .CoflCommand<RateCommand>(
                $" {greenHeard}  upvote flip",
                $"{flip.Auction.Uuid} {flip.Finder} up",
                "Vote this flip up").Break;

            response = AddBlacklistActions(response, flip.Auction, flip.Auction.AuctioneerId).Break
            .CoflCommand<TimeCommand>(
                timingMessage,
                $"{flip.Auction.Uuid}",
                "Get your timings for flip").Break;

            response = AddSellerActions(response, flip.Auction).Break
            .CoflCommand<ReferenceCommand>(
                $"{McColorCodes.WHITE}[?]{McColorCodes.GRAY} Get references",
                $"{flip.Auction.Uuid}",
                "Find out why this was deemed a flip").Break;

            response = AddWebsiteAction(response, $"https://sky.coflnet.com/a/{flip.Auction.Uuid}", " ➹  Open on website", "Open link");

            if (context.socket.GetService<Services.ModeratorService>().IsModerator(context.socket))
                response.Msg(McColorCodes.DARK_GRAY + " . ", null, flip.Auction.Context?.GetValueOrDefault("pre-api", flip.AdditionalProps?.GetValueOrDefault("server", "non")) ?? "no context");
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
