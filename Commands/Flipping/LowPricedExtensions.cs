using Coflnet.Sky.Core;

public static class LowPricedExtensions
{
    public static bool IsPreApi(this LowPricedAuction flip)
    {
        return (flip.Auction.Context?.TryGetValue("pre-api", out var preApi) ?? false) && preApi != "recheck";
    }
}