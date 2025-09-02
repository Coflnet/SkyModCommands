using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class BackwaterBayouTask : IslandTask
{
    protected override string RegionName => "backwater bayou";
    protected override HashSet<string> locationNames =>
    [
        "Bayou Outpost",
        "Bayou Swamp",
        "The Bayou"
    ];
}
