using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class SpidersDenTask : IslandTask
{
    protected override string RegionName => "spider's den";
    protected override HashSet<string> locationNames =>
    [
        "Arachne's Sanctuary",
        "Archaeologist's Camp",
        "Spider Mound",
        "The Spider's Den"
    ];
}
