using System;
using Coflnet.Sky.Core;

#nullable enable

namespace Coflnet.Sky.ModCommands.Services.Donut;

public static class DonutServerContext
{
    public const string Name = "donut";

    public static bool IsDonut(string? serverContext)
    {
        return string.Equals(serverContext, Name, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDonut(LowPricedAuction? flip)
    {
        if (flip?.AdditionalProps?.TryGetValue("server", out var additionalServerContext) == true)
            return IsDonut(additionalServerContext);
        if (flip?.Auction?.Context?.TryGetValue("server", out var auctionServerContext) == true)
            return IsDonut(auctionServerContext);
        return false;
    }
}