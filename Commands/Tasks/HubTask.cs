using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class HubTask : IslandTask
{
    protected override string RegionName => "hub";
    protected override HashSet<string> locationNames =>
    [
        "Hub",
        "Auction House",
        "Bank",
        "Bazaar Alley",
        "Blacksmith",
        "Canvas Room",
        "Colosseum",
        "Community Center",
        "Farm",
        "Fashion Shop",
        "Flower House",
        "Forest",
        "Graveyard",
        "Hexatorum",
        "Library",
        "Mountain",
        "Museum",
        "Pet Care",
        "Ruins",
        "Tavern",
        "Thaumaturgist",
        "Village",
        "Wilderness",
        "Wizard Tower"
    ];
}
