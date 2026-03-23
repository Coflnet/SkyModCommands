using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── End island methods ──
public class ZealotsFdTask : MethodTask
{
    protected override string MethodName => "Zealots (FD)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["SUMMONING_EYE", "ENDER_PEARL"];
    protected override List<MethodDrop> FormulaDrops => [new("SUMMONING_EYE", 4), new("ENDER_PEARL", 300)];
}

// ── Mushroom Desert farming ──
public class RedMushroomTask : MethodTask
{
    protected override string MethodName => "Red Mushroom";
    protected override HashSet<string> Locations => ["Mushroom Desert", "Glowing Mushroom Cave", "Mushroom Gorge"];
    protected override HashSet<string> DetectionItems => ["RED_MUSHROOM", "ENCHANTED_RED_MUSHROOM"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_RED_MUSHROOM", 300)];
}
public class BrownMushroomTask : MethodTask
{
    protected override string MethodName => "Brown Mushroom";
    protected override HashSet<string> Locations => ["Mushroom Desert", "Glowing Mushroom Cave", "Mushroom Gorge"];
    protected override HashSet<string> DetectionItems => ["BROWN_MUSHROOM", "ENCHANTED_BROWN_MUSHROOM"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_BROWN_MUSHROOM", 300)];
}
public class MyceliumTask : MethodTask
{
    protected override string MethodName => "Mycelium";
    protected override HashSet<string> Locations => ["Mushroom Desert", "Glowing Mushroom Cave"];
    protected override HashSet<string> DetectionItems => ["MYCEL", "ENCHANTED_MYCELIUM"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MYCELIUM", 250)];
}
