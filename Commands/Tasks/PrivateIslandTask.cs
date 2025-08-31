using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class PrivateIslandTask : IslandTask
{
    protected override string RegionName => "private island";
    protected override HashSet<string> locationNames =>
    [
        "Private Island",
        "Your Island"
    ];
}
