using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class CrimsonIsleTask : IslandTask
{
    protected override string RegionName => "crimson isle";
    protected override HashSet<string> locationNames =>
    [
        "Smoldering Tomb",
        "The Bastion",
        "Scarleton",
        "Dragontail",
        "Aura's Lab",
        "Belly of the Beast",
        "Blazing Volcano",
        "Burning Desert",
        "Cathedral",
        "Chief's Hut",
        "Community Center",
        "Courtyard",
        "Dragontail Auction House",
        "Dragontail Bank",
        "Dragontail Blacksmith",
        "Dragontail Town Square",
        "Dojo",
        "Forgotten Skull",
        "Igor's Workshop",
        "Mage Council",
        "Mage Outpost",
        "Matriarch's Lair",
        "Minion Shop",
        "Mystic Marsh",
        "Odger's Hut",
        "Plhlegblast Airport",
        "Ruins of Ashfang",
        "Scarleton Auction House",
        "Scarleton Bank",
        "Scarleton Blacksmith",
        "Scarleton Plaza",
        "Scarleton Town Square",
        "Stronghold",
        "The Dukedom",
        "Throne Room",
        "Volcano Cave",
        "Volcano Manor"
    ];
}

public class CrimsonIsleFishingTask : IslandTask
{
    protected override string RegionName => "crimson isle fishing";
    protected override HashSet<string> locationNames =>
    [
        "Volcano Cave",
        "Burning Desert",
        "Mystic Marsh"
    ];
}
