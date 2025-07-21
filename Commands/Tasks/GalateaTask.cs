using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class GalateaDivingTask : IslandTask
{
    protected override string RegionName => "galatea";
    protected override HashSet<string> locationNames =>
    [
        "Driptoad Delve",
        "Ancient Ruins",
        "Dive-Ember Pass",
        "Drowned Reliquary",
        "Kelpwoven Tunnels",
        "Murkwater Depths",
        "Murkwater Shallows",
        "Reefguard Pass",
        "Squid Cave"
    ];
}
public class GalateaFishingTask : IslandTask
{
    protected override string RegionName => "galatea";
    protected override HashSet<string> locationNames =>
    [
        "Driptoad Delve"
    ];
}
public class GalateaTask : IslandTask
{
    protected override string RegionName => "galatea";
    protected override HashSet<string> locationNames =>
    [
        "Ancient Ruins",
        "Evergreen Plateau",
        "Fusion House",
        "Murkwater Outpost",
        "North Wetlands",
        "North Reaches",
        "Red House",
        "Moonglade Marsh",
        "Murkwater Loch", // down but outside of water
        "Reafguard Pass",
        "Side-Ember Way",
        "South Reaches",
        "South Wetlands",
        "Stride-Ember Fissure",
        "Tangleburg",
        "Tangleburg Bank",
        "Tangleburg's Path",
        "Tomb Floodway",
        "Tranquil Pass",
        "Tranquility Sanctum",
        "Verdant Summit",
        "West Reaches",
        "Wyrmgrove Tomb"
    ];
}
