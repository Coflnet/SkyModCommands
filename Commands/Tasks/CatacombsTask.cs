using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class CatacombsTask : IslandTask
{
    protected override string RegionName => "the catacombs";
    protected override HashSet<string> locationNames =>
    [
        "The Catacombs",
        "Entrance",
        "Floor I",
        "Floor II",
        "Floor III",
        "Floor IV",
        "Floor V",
        "Floor VI",
        "Floor VII",
        "Master Mode"
    ];
}
