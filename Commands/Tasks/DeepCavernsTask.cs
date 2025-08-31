using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class DeepCavernsTask : IslandTask
{
    protected override string RegionName => "deep caverns";
    protected override HashSet<string> locationNames =>
    [
        "Deep Caverns",
        "Diamond Reserve",
        "Emerald Reserve",
        "Gold Reserve",
        "Gunpowder Mines",
        "Lapis Quarry",
        "Obsidian Sanctuary",
        "Pigman's Den",
        "Redstone Quarry",
        "Slimehill"
    ];
}
