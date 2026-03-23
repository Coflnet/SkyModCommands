using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class PestTask : MethodTask
{
    protected override string MethodName => "Pest";
    protected override HashSet<string> Locations => ["The Garden", "Plot 1", "Plot 2", "Plot 3", "Plot 4", "Plot 5", "Plot 6", "Plot 7", "Plot 8", "Plot 9", "Plot 10", "Plot 11", "Plot 12"];
    protected override HashSet<string> DetectionItems => ["PEST_KILL", "PESTERMINATOR"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_CROP", 80)];
}
public class FigTask : MethodTask
{
    protected override string MethodName => "Fig";
    protected override HashSet<string> Locations => ["The Garden", "Plot 1", "Plot 2", "Plot 3", "Plot 4", "Plot 5", "Plot 6", "Plot 7", "Plot 8", "Plot 9", "Plot 10", "Plot 11", "Plot 12"];
    protected override HashSet<string> DetectionItems => ["FIG", "ENCHANTED_FIG"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_FIG", 200)];
}
