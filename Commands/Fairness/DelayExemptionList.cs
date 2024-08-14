using System;
using System.Collections.Generic;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;

public interface IDelayExemptList
{
    public HashSet<(string,string)> Exemptions { get; set; }
    bool IsExempt(LowPricedAuction flipInstance);
}

public class DelayExemptionList : IDelayExemptList
{
    public HashSet<(string, string)> Exemptions { get; set; } = [];

    public bool IsExempt(LowPricedAuction flipInstance)
    {
        var exempted = Exemptions.Contains((flipInstance.Auction.Tag, flipInstance.AdditionalProps.GetValueOrDefault("key", "nope")));
        if (exempted)
        {
            Console.WriteLine($"Exempted {flipInstance.Auction.Tag} {flipInstance.AdditionalProps.GetValueOrDefault("key", "nope")}");
        }
        return exempted;
    }
}

