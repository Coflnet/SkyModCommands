using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class RiftTask : IslandTask
{
    protected override string RegionName => "the rift";
    protected override HashSet<string> locationNames =>
    [
        "Rift",
        "Black Lagoon",
        "Broken Cage",
        "Colosseum",
        "Déjà Vu Alley",
        "Dollhouse",
        "Enigma's Crib",
        "Half-Eaten Cave",
        "Lagune",
        "Mirrorverse",
        "Murder House",
        "Otherworld",
        "Rift Gallery",
        "Stillgore Château",
        "Tayber's Lab",
        "The Bastion",
        "The Rift",
        "West Village",
        "Wyld Woods"
    ];
}
