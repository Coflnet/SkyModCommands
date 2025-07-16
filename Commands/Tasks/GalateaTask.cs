using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class GalateaTask : IslandTask
{
    protected override string RegionName => "galatea";
    protected override HashSet<string> locationNames =>
    [
        "Ancient Ruins",
        "Evergreen Plateau",
        "Dive-Ember Pass",
        "Driptoad Delve",
        "Drowned Reliquary",
        "Fusion House",
        "Kelpwoven Tunnels",
        "Moonglade Marsh",
        "Murkwater Depths",
        "Murkwater Shallows",
        "Murkwater Loch",
        "Murkwater Outpost",
        "North Wetlands",
        "North Reaches",
        "Red House",
        "Reafguard Pass",
        "Side-Ember Way",
        "South Reaches",
        "South Wetlands",
        "Stride-Ember Fissure",
        "Squid Cave",
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
