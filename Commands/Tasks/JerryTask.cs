using System;
using System.Collections.Generic;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class JerryTask : IslandTask
{
    AdjustedEventfilter eventFiler = new();
    protected override string RegionName => "jerry";
    protected override HashSet<string> locationNames =>
    [
        "Jerry's Workshop",
        "Jerry's House",
        "Jerry's Pond",
        "Jerry's Village",
        "Jerry's Castle"
    ];

    public override bool IsPossibleAt(DateTime time)
    {
        eventFiler.now = time;
        return eventFiler.GetExpression(null, CurrentEventDetailedFlipFilter.Events.SeasonOfJerry.ToString()).Compile()(null);
    }

    private class AdjustedEventfilter : CurrentEventDetailedFlipFilter
    {
        public DateTime now { get; set; }
        protected override DateTime Now => now;
    }
}