using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class FarmingIslandsTask : IslandTask
{
    protected override string RegionName => "farming islands";
    protected override HashSet<string> locationNames =>
    [
        "The Barn",
        "Mushroom Desert",
        "Oasis",
        "Overgrown Grass",
        "Trapper's Den"
    ];
}
