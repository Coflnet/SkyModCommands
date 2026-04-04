using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Base for Diana tasks — only accessible when the current mayor is Diana
/// </summary>
public abstract class BaseDianaTask : MethodTask
{
    protected override string Category => "Event";
    protected override string CheckAccessibility(TaskParams parameters)
    {
        if (parameters.CurrentMayor != null && parameters.CurrentMayor != "diana")
            return $"Only available when Diana is mayor (current: {parameters.CurrentMayor}).";
        return base.CheckAccessibility(parameters);
    }
}

public class DianaTask : BaseDianaTask
{
    protected override string MethodName => "Diana";
    protected override HashSet<string> Locations => ["Hub", "Wilderness", "Forest", "Mountain", "Ruins", "Graveyard", "Farm", "Village"];
    protected override HashSet<string> DetectionItems => ["GRIFFIN_FEATHER", "MINOS_RELIC", "DAEDALUS_STICK", "CHIMERA"];
    protected override List<MethodDrop> FormulaDrops => [new("GRIFFIN_FEATHER", 30), new("DAEDALUS_STICK", 2)];
    protected override string HowTo => "During the Mythological Ritual mayor event, use an Ancestral Spade on Diana burrows in the Hub. Follow particle trails to dig burrows.";
    protected override List<RequiredItem> RequiredItems => [
        new() { ItemTag = "ANCESTRAL_SPADE", Reason = "Required to dig Diana burrows" },
        new() { ItemTag = "GRIFFIN_PET", Reason = "Griffin pet reveals burrow locations via particles" }
    ];
    protected override List<DropEffect> Effects => [
        new() { Name = "Griffin Pet Rarity", Description = "Higher rarity Griffin reveals burrows further away", EstimatedMultiplier = 1.3 },
        new() { Name = "Magic Find", Description = "Increases chance of Chimera book and Daedalus Stick", EstimatedMultiplier = 1.25 }
    ];
}
public class DianaHuntingTask : BaseDianaTask
{
    protected override string MethodName => "Diana (Hunting)";
    protected override HashSet<string> Locations => ["Hub", "Wilderness", "Forest", "Mountain", "Ruins", "Graveyard", "Farm", "Village"];
    protected override HashSet<string> DetectionItems => ["SHARD_KING_MINOS", "MINOS_CHAMPION", "MINOS_INQUISITOR"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_KING_MINOS", 15), new("GRIFFIN_FEATHER", 40), new("DAEDALUS_STICK", 3)];
    protected override string HowTo => "During Mythological Ritual, focus on killing Mythological mobs (Minotaurs, Gaia, Champions, Inquisitors). Inquisitors drop the most money.";
    protected override List<RequiredItem> RequiredItems => [
        new() { ItemTag = "ANCESTRAL_SPADE", Reason = "Required to dig Diana burrows" },
        new() { ItemTag = "GRIFFIN_PET", Reason = "Griffin pet reveals burrow locations" },
        new() { ItemTag = "CHIMERA", Reason = "High-value drop from Inquisitors" }
    ];
    protected override List<DropEffect> Effects => [
        new() { Name = "Griffin Pet Rarity", Description = "Higher rarity Griffin reveals burrows further", EstimatedMultiplier = 1.3 },
        new() { Name = "Magic Find", Description = "Increases rare mob spawn and drop chance", EstimatedMultiplier = 1.3 },
        new() { Name = "Ferocity", Description = "More hits per attack for faster kills", EstimatedMultiplier = 1.15 }
    ];
}
