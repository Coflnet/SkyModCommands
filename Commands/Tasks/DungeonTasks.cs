using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class M4Task : MethodTask
{
    protected override string MethodName => "M4";
    protected override HashSet<string> Locations => ["The Catacombs", "Master Mode Catacombs Floor IV"];
    protected override List<MethodDrop> FormulaDrops => [new("WITHER_ESSENCE", 300)];
}
public class M5Task : MethodTask
{
    protected override string MethodName => "M5";
    protected override HashSet<string> Locations => ["The Catacombs", "Master Mode Catacombs Floor V"];
    protected override List<MethodDrop> FormulaDrops => [new("WITHER_ESSENCE", 400)];
}
public class M6Task : MethodTask
{
    protected override string MethodName => "M6";
    protected override HashSet<string> Locations => ["The Catacombs", "Master Mode Catacombs Floor VI"];
    protected override List<MethodDrop> FormulaDrops => [new("WITHER_ESSENCE", 500)];
}
public class M7Task : MethodTask
{
    protected override string MethodName => "M7";
    protected override HashSet<string> Locations => ["The Catacombs", "Master Mode", "Floor VII", "Master Mode Catacombs Floor VII"];
    protected override List<MethodDrop> FormulaDrops => [new("WITHER_ESSENCE", 600), new("NECRON_HANDLE", 0.05)];
}
public class M7KismetTask : MethodTask
{
    protected override string MethodName => "M7 (Kismet)";
    protected override HashSet<string> Locations => ["The Catacombs", "Master Mode", "Floor VII", "Master Mode Catacombs Floor VII"];
    protected override HashSet<string> DetectionItems => ["KISMET_FEATHER"];
    protected override List<MethodDrop> FormulaDrops => [new("WITHER_ESSENCE", 600), new("NECRON_HANDLE", 0.1)];
}
