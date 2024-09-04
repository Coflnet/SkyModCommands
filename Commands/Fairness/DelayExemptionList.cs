using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;

public interface IDelayExemptList
{
    public HashSet<(string, string)> Exemptions { get; set; }
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
            if (Activity.Current?.Tags.Contains(new KeyValuePair<string, string>("exempted", "true")) == false)
            {
                Activity.Current?.AddTag("exempted", "true");
                Activity.Current.Log($"Exempted {flipInstance.Auction.Tag} {flipInstance.AdditionalProps.GetValueOrDefault("key", "nope")}");
            }
        }
        return exempted;
    }
}

