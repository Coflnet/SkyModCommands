using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── End island methods ──
public class ZealotsFdTask : MethodTask
{
    protected override string MethodName => "Zealots (FD)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["SUMMONING_EYE", "ENDER_PEARL"];
    protected override List<MethodDrop> FormulaDrops => [new("SUMMONING_EYE", 4), new("ENDER_PEARL", 300)];
    protected override string Category => "Combat";
    protected override string HowTo => "Go to The End and grind Zealots for Summoning Eyes. Kill Zealots rapidly; Special Zealots have a guaranteed eye drop. Formula-based estimate.";
    protected override List<RequiredItem> RequiredItems => [
        new() { ItemTag = "ASPECT_OF_THE_DRAGONS", Reason = "One-shot zealots for fast farming" }
    ];
    protected override List<DropEffect> Effects => [
        new() { Name = "Magic Find", Description = "Increases Summoning Eye drop rate", EstimatedMultiplier = 1.2 },
        new() { Name = "Combat Level", Description = "Higher combat level increases damage", EstimatedMultiplier = 1.1 }
    ];
}

// ── Mushroom Desert farming ──
public class RedMushroomTask : MethodTask
{
    protected override string MethodName => "Red Mushroom";
    protected override HashSet<string> Locations => ["Mushroom Desert", "Glowing Mushroom Cave", "Mushroom Gorge"];
    protected override HashSet<string> DetectionItems => ["RED_MUSHROOM", "ENCHANTED_RED_MUSHROOM"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_RED_MUSHROOM", 300)];
    protected override string Category => "Farming";
    protected override string HowTo => "Go to the Mushroom Desert and farm Red Mushrooms. Break mushroom blocks in the Glowing Mushroom Cave for fast collection.";
    protected override List<DropEffect> Effects => [
        new() { Name = "Farming Fortune", Description = "Increases mushroom drop amount", EstimatedMultiplier = 1.3 },
        new() { Name = "Farming Speed", Description = "Break blocks faster for more per hour", EstimatedMultiplier = 1.2 }
    ];
}
public class BrownMushroomTask : MethodTask
{
    protected override string MethodName => "Brown Mushroom";
    protected override HashSet<string> Locations => ["Mushroom Desert", "Glowing Mushroom Cave", "Mushroom Gorge"];
    protected override HashSet<string> DetectionItems => ["BROWN_MUSHROOM", "ENCHANTED_BROWN_MUSHROOM"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_BROWN_MUSHROOM", 300)];
    protected override string Category => "Farming";
    protected override string HowTo => "Go to the Mushroom Desert and farm Brown Mushrooms. Same areas as Red Mushrooms but collect the brown variant.";
    protected override List<DropEffect> Effects => [
        new() { Name = "Farming Fortune", Description = "Increases mushroom drop amount", EstimatedMultiplier = 1.3 },
        new() { Name = "Farming Speed", Description = "Break blocks faster for more per hour", EstimatedMultiplier = 1.2 }
    ];
}
public class MyceliumTask : MethodTask
{
    protected override string MethodName => "Mycelium";
    protected override HashSet<string> Locations => ["Mushroom Desert", "Glowing Mushroom Cave"];
    protected override HashSet<string> DetectionItems => ["MYCEL", "ENCHANTED_MYCELIUM"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MYCELIUM", 250)];
    protected override string Category => "Farming";
    protected override string HowTo => "Go to the Mushroom Desert and mine Mycelium blocks. Found on the ground in mushroom biome areas.";
    protected override List<DropEffect> Effects => [
        new() { Name = "Mining Fortune", Description = "Increases mycelium drop rate", EstimatedMultiplier = 1.2 },
        new() { Name = "Mining Speed", Description = "Break mycelium blocks faster", EstimatedMultiplier = 1.2 }
    ];
}
