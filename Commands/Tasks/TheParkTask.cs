using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class TheParkTask : IslandTask
{
    protected override string RegionName => "the park";
    protected override HashSet<string> locationNames =>
    [
        "Birch Park",
        "Dark Thicket",
        "Howling Cave",
        "Jungle Island",
        "Melody's Plateau",
        "Savanna Woodland",
        "Spruce Woods",
        "The Howling Cave",
        "The Wolf's Den"
    ];
}

public class TheParkForagingTask : IslandTask
{
    protected override string RegionName => "the park foraging";
    protected override HashSet<string> locationNames =>
    [
        "Birch Park",
        "Dark Thicket",
        "Savanna Woodland",
        "Spruce Woods"
    ];
}
