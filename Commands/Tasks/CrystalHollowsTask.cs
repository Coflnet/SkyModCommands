using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class CrystalHollowsTask : IslandTask
{
    protected override string RegionName => "crystal hollows";
    protected override HashSet<string> locationNames =>
    [
        "Crystal Hollows",
        "Crystal Nucleus",
        "Dragon's Lair",
        "Fairy Grotto",
        "Goblin Holdout",
        "Goblin Queen's Den",
        "Jungle Temple",
        "Lost Precursor City",
        "Magma Fields",
        "Mithril Deposits",
        "Precursor Remnants"
    ];
}
