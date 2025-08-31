using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class DwarvenMinesTask : IslandTask
{
    protected override string RegionName => "dwarven mines";
    protected override HashSet<string> locationNames =>
    [
        "Aristocrat's Passage",
        "Barracks of Heroes",
        "CC Inc.",
        "Cliffside Veins",
        "Commission's Office",
        "Dwarven Tavern",
        "Dwarven Village",
        "Far Reserve",
        "First-Class Lounge",
        "Forge",
        "Forge Basin",
        "Gates to the Mines",
        "Grand Library",
        "Great Ice Wall",
        "Hanging Court",
        "Lava Springs",
        "Miner's Guild",
        "Palace Bridge",
        "Puzzler's Hideout",
        "Rampart's Quarry",
        "Royal Mines",
        "Royal Palace",
        "Royal Quarters",
        "The Great Forge",
        "The Lift",
        "The Mist",
        "Upper Mines"
    ];
}

public class DwarvenMinesMiningTask : IslandTask
{
    protected override string RegionName => "dwarven mines mining";
    protected override HashSet<string> locationNames =>
    [
        "Cliffside Veins",
        "Far Reserve",
        "Rampart's Quarry",
        "Royal Mines",
        "The Mist",
        "Upper Mines"
    ];
}
