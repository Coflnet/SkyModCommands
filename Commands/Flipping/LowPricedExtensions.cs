using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

public static class LowPricedExtensions
{
    public static bool IsPreApi(this LowPricedAuction flip)
    {
        return (flip.Auction.Context?.TryGetValue("pre-api", out var preApi) ?? false) && !preApi.Contains("recheck");
    }

    public static LowPricedAuction ToLowPriced(this FlipInstance flip)
    {
        return new LowPricedAuction()
        {
            Auction = flip.Auction,
            Finder = flip.Finder,
            TargetPrice = flip.Target,
            DailyVolume = flip.Volume,
            AdditionalProps = flip.Context
        };
    }
}