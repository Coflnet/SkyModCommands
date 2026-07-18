using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.ModCommands.Dialogs;

/// <summary>
/// Actions for a lowball offer. Unlike auction flips, lowball offers have no timing data.
/// </summary>
public class LowballOptionsDialog : OfferOptionsDialog
{
    public override ChatPart[] GetResponse(DialogArgs context)
    {
        var offer = context.socket.GetService<LowballSerivce>().GetOfferForMenu(context.Context);
        if (offer == null)
            return New().MsgLine("This lowball offer is no longer available. New offers keep their action menu for 30 minutes.");

        var response = AddBlacklistActions(
                New().MsgLine("What do you want to do with this lowball offer?"),
                offer.Auction,
                offer.SellerName)
            .Break;
        response = AddSellerActions(response, offer.Auction).Break;

        var lowballFlip = LowballSerivce.ToFlip(offer);
        var matchingWhitelist = context.socket.Settings.WhiteList
            .FirstOrDefault(entry => WhichBLEntryCommand.Matches(context.socket.SessionInfo, lowballFlip, entry));
        if (matchingWhitelist != null)
        {
            response = response.CoflCommand<WhitelistCommand>(
                $" {McColorCodes.GREEN}Matched your whitelist, click to remove it",
                "rm " + BlacklistCommand.FormatId(matchingWhitelist),
                "This offer matched this whitelist entry. Click to remove it.")
                .Break;
        }

        return AddWebsiteAction(
            response,
            $"https://sky.coflnet.com/item/{offer.Auction.Tag}",
            " ➹  Open item on website",
            "Open the item's page on SkyCofl");
    }
}
