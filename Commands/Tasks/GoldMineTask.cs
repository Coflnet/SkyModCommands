using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class GoldMineTask : IslandTask
{
    protected override string RegionName => "gold mine";
    protected override HashSet<string> locationNames =>
    [
        "Gold Mine"
    ];
}
