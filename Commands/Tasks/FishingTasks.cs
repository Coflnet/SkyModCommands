using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── Base for all fishing tasks with shared metadata ──
public abstract class BaseFishingTask : MethodTask
{
    protected override string Category => "Fishing";
    protected override string ActionUnit => "catches";
    protected override List<DropEffect> Effects =>
    [
        new() { Name = "Fishing Speed", Description = "Higher fishing speed reduces time between catches", EstimatedMultiplier = 1.3 },
        new() { Name = "Sea Creature Chance", Description = "Increases rare sea creature spawn rate (hunting)", EstimatedMultiplier = 1.2 },
        new() { Name = "Lure enchantment", Description = "Reduces time between bites", EstimatedMultiplier = 1.15 }
    ];
}

// ── Regular Fishing (no sea creature hunting) ──
public class PiscaryFishingTask : BaseFishingTask
{
    protected override string MethodName => "Piscary Fishing";
    protected override HashSet<string> Locations => ["Piscary"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 250), new("ENCHANTED_RAW_FISH", 25)];
    protected override double ActionsPerHour => 275;
    protected override string HowTo => "Go to Piscary on Galatea and fish. Use a fishing rod with Lure and Blessing enchantments for best results.";
    protected override List<RequiredItem> RequiredItems => [new() { ItemTag = "ROD_OF_THE_SEA", Reason = "Fishing rod" }];
}
public class BayouFishingTask : BaseFishingTask
{
    protected override string MethodName => "Bayou Fishing";
    protected override HashSet<string> Locations => ["Bayou Outpost", "Bayou Swamp", "The Bayou"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200), new("LILY_PAD", 80)];
}
public class BayouHotspotFishingTask : BaseFishingTask
{
    protected override string MethodName => "Bayou Hotspot Fishing";
    protected override HashSet<string> Locations => ["Bayou Outpost", "Bayou Swamp", "The Bayou"];
    protected override HashSet<string> DetectionItems => ["HOTSPOT_CATCH"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 300), new("LILY_PAD", 120)];
}
public class SpookyFishingTask : BaseFishingTask
{
    protected override string MethodName => "Spooky Fishing";
    protected override HashSet<string> Locations => ["Spooky Festival", "Spooky Pier", "The Park", "Howling Cave"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200), new("PUMPKIN", 50)];
}
public class WinterFishingTask : BaseFishingTask
{
    protected override string MethodName => "Winter Fishing";
    protected override HashSet<string> Locations => ["Jerry's Workshop", "Jerry Pond", "Hot Springs"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 220), new("ICE", 100)];
}
public class WaterWormFishingTask : BaseFishingTask
{
    protected override string MethodName => "Water Worm Fishing";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Lost Precursor City", "Fairy Grotto", "Goblin Holdout", "Magma Fields", "Mithril Deposits"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 180)];
}
public class QuarryFishingTask : BaseFishingTask
{
    protected override string MethodName => "Quarry Fishing";
    protected override HashSet<string> Locations => ["The Quarry", "Quarry"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class CrimsonFishingTask : BaseFishingTask
{
    protected override string MethodName => "Crimson Fishing";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Mystic Marsh", "Volcano Cave"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("MAGMA_FISH", 150), new("ENCHANTED_MAGMAFISH", 15)];
}
public class CrimsonHotspotFishingTask : BaseFishingTask
{
    protected override string MethodName => "Crimson Hotspot Fishing";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Mystic Marsh", "Volcano Cave"];
    protected override HashSet<string> DetectionItems => ["HOTSPOT_CATCH"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("MAGMA_FISH", 200), new("ENCHANTED_MAGMAFISH", 20)];
}
public class FestivalFishingTask : BaseFishingTask
{
    protected override string MethodName => "Festival Fishing";
    protected override HashSet<string> Locations => ["Festival Plaza", "Jerry's Workshop"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class SquidFishingTask : BaseFishingTask
{
    protected override string MethodName => "Squid Fishing";
    protected override HashSet<string> Locations => ["Squid Cave", "Murkwater Depths", "Murkwater Shallows", "Driptoad Delve"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("INK_SACK", 200), new("ENCHANTED_INK_SACK", 20)];
}
public class GalateaFishingMethodTask : BaseFishingTask
{
    protected override string MethodName => "Galatea Fishing";
    protected override HashSet<string> Locations => ["Driptoad Delve", "Murkwater Depths", "Murkwater Shallows", "Squid Cave", "Reefguard Pass"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("SEA_LUMIES", 150), new("RAW_FISH", 180)];
}
public class OasisFishingTask : BaseFishingTask
{
    protected override string MethodName => "Oasis Fishing";
    protected override HashSet<string> Locations => ["Oasis", "Mushroom Desert"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class WaterFishingTask : BaseFishingTask
{
    protected override string MethodName => "Water Fishing";
    protected override HashSet<string> Locations => ["Hub", "Village", "Forest", "Birch Park", "The Park"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class MagmaCoreFishingTask : BaseFishingTask
{
    protected override string MethodName => "Magma Core Fishing";
    protected override HashSet<string> Locations => ["Magma Fields", "Crystal Hollows"];
    protected override HashSet<string> DetectionItems => ["MAGMA_CORE"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("MAGMA_CORE", 8)];
}
public class FlamingWormFishingTask : BaseFishingTask
{
    protected override string MethodName => "Flaming Worm Fishing";
    protected override HashSet<string> Locations => ["Magma Fields", "Crystal Hollows", "Blazing Volcano"];
    protected override HashSet<string> DetectionItems => ["FLAMING_WORM"];
    protected override List<MethodDrop> FormulaDrops => [new("FLAMING_WORM", 30)];
}

// ── Fishing with Sea Creature Hunting ──
public class PiscaryFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Piscary Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Piscary"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 250), new("ENCHANTED_RAW_FISH", 25)];
}
public class BayouFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Bayou Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Bayou Outpost", "Bayou Swamp", "The Bayou"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200), new("LILY_PAD", 80)];
}
public class BayouHotspotFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Bayou Hotspot Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Bayou Outpost", "Bayou Swamp", "The Bayou"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 300), new("LILY_PAD", 120)];
}
public class SpookyFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Spooky Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Spooky Festival", "Spooky Pier", "The Park", "Howling Cave"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200), new("PUMPKIN", 50)];
}
public class WinterFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Winter Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Jerry's Workshop", "Jerry Pond", "Hot Springs"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 220), new("ICE", 100)];
}
public class WaterWormFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Water Worm Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Lost Precursor City", "Fairy Grotto", "Goblin Holdout", "Magma Fields", "Mithril Deposits"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 180)];
}
public class QuarryFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Quarry Fishing (Hunting)";
    protected override HashSet<string> Locations => ["The Quarry", "Quarry"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class CrimsonFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Crimson Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Mystic Marsh", "Volcano Cave"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("MAGMA_FISH", 150), new("ENCHANTED_MAGMAFISH", 15)];
}
public class FestivalFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Festival Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Festival Plaza", "Jerry's Workshop"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class SquidFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Squid Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Squid Cave", "Murkwater Depths", "Murkwater Shallows", "Driptoad Delve"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("INK_SACK", 200), new("ENCHANTED_INK_SACK", 20)];
}
public class GalateaFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Galatea Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Driptoad Delve", "Murkwater Depths", "Murkwater Shallows", "Squid Cave", "Reefguard Pass"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("SEA_LUMIES", 150), new("RAW_FISH", 180)];
}
public class OasisFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Oasis Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Oasis", "Mushroom Desert"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class WaterFishingHuntingTask : BaseFishingTask
{
    protected override string MethodName => "Water Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Hub", "Village", "Forest", "Birch Park", "The Park"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
