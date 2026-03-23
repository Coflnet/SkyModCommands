using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class DianaTask : MethodTask
{
    protected override string MethodName => "Diana";
    protected override HashSet<string> Locations => ["Hub", "Wilderness", "Forest", "Mountain", "Ruins", "Graveyard", "Farm", "Village"];
    protected override HashSet<string> DetectionItems => ["GRIFFIN_FEATHER", "MINOS_RELIC", "DAEDALUS_STICK", "CHIMERA"];
    protected override List<MethodDrop> FormulaDrops => [new("GRIFFIN_FEATHER", 30), new("DAEDALUS_STICK", 2)];
}
public class DianaHuntingTask : MethodTask
{
    protected override string MethodName => "Diana (Hunting)";
    protected override HashSet<string> Locations => ["Hub", "Wilderness", "Forest", "Mountain", "Ruins", "Graveyard", "Farm", "Village"];
    protected override HashSet<string> DetectionItems => ["SHARD_KING_MINOS", "MINOS_CHAMPION", "MINOS_INQUISITOR"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_KING_MINOS", 15), new("GRIFFIN_FEATHER", 40), new("DAEDALUS_STICK", 3)];
}
