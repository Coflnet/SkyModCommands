using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.ModCommands.Dialogs;

/// <summary>
/// Shared item and seller actions for auction flips and lowball offers.
/// </summary>
public abstract class OfferOptionsDialog : Dialog
{
    protected static DialogBuilder AddBlacklistActions(DialogBuilder response, SaveAuction auction, string sellerName)
    {
        var itemName = ItemReferences.RemoveReforgesAndLevel(auction.ItemName);
        var seller = sellerName ?? auction.AuctioneerId;
        var redX = McColorCodes.DARK_RED + "✖" + McColorCodes.GRAY;

        return response
            .CoflCommand<BlacklistCommand>(
                $" {redX}  Blacklist this item",
                $"add {auction.Tag} forceBlacklist=true",
                $"Don't show {McColorCodes.AQUA}{itemName}{McColorCodes.RED} AT ALL anymore")
            .CoflCommand<BlacklistCommand>(
                $" {McColorCodes.GREEN}for 1week,",
                $"add {auction.Tag} forceBlacklist=true duration=7d",
                $"Don't show {McColorCodes.AQUA}{itemName}{McColorCodes.GREEN} for a week")
            .CoflCommand<BlacklistCommand>(
                $" {McColorCodes.GREEN}{McColorCodes.ITALIC}1 day",
                $"add {auction.Tag} forceBlacklist=true duration=1d",
                $"Don't show {McColorCodes.AQUA}{itemName}{McColorCodes.GREEN} for 24 hours")
            .CoflCommand<BlacklistCommand>(
                $" {McColorCodes.YELLOW}seller",
                $"add seller={auction.AuctioneerId} forceBlacklist=true",
                $"Don't show seller {McColorCodes.AQUA}{seller}{McColorCodes.YELLOW} AT ALL anymore")
            .CoflCommand<BlacklistCommand>(
                $" {McColorCodes.YELLOW}{McColorCodes.ITALIC}1 day",
                $"add seller={auction.AuctioneerId} forceBlacklist=true duration=1d",
                $"Don't show seller {McColorCodes.AQUA}{seller}{McColorCodes.YELLOW} for a day");
    }

    protected static DialogBuilder AddSellerActions(DialogBuilder response, SaveAuction auction)
    {
        return response
            .CoflCommand<AhOpenCommand>(
                $"{McColorCodes.GOLD} AH {McColorCodes.GRAY}open seller's ah ",
                auction.AuctioneerId,
                "Open the seller's ah")
            .CoflCommand<GetMcNameForCommand>(
                McColorCodes.DARK_GREEN + " Get Name",
                auction.AuctioneerId,
                "Get the name of the seller");
    }

    protected static DialogBuilder AddWebsiteAction(DialogBuilder response, string url, string label, string hover)
    {
        return response.MsgLine(label, url, hover);
    }
}
