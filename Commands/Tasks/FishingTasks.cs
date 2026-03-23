using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── Regular Fishing (no sea creature hunting) ──
public class PiscaryFishingTask : MethodTask
{
    protected override string MethodName => "Piscary Fishing";
    protected override HashSet<string> Locations => ["Piscary"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 250), new("ENCHANTED_RAW_FISH", 25)];
}
public class BayouFishingTask : MethodTask
{
    protected override string MethodName => "Bayou Fishing";
    protected override HashSet<string> Locations => ["Bayou Outpost", "Bayou Swamp", "The Bayou"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200), new("LILY_PAD", 80)];
}
public class BayouHotspotFishingTask : MethodTask
{
    protected override string MethodName => "Bayou Hotspot Fishing";
    protected override HashSet<string> Locations => ["Bayou Outpost", "Bayou Swamp", "The Bayou"];
    protected override HashSet<string> DetectionItems => ["HOTSPOT_CATCH"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 300), new("LILY_PAD", 120)];
}
public class SpookyFishingTask : MethodTask
{
    protected override string MethodName => "Spooky Fishing";
    protected override HashSet<string> Locations => ["Spooky Festival", "Spooky Pier", "The Park", "Howling Cave"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200), new("PUMPKIN", 50)];
}
public class WinterFishingTask : MethodTask
{
    protected override string MethodName => "Winter Fishing";
    protected override HashSet<string> Locations => ["Jerry's Workshop", "Jerry Pond", "Hot Springs"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 220), new("ICE", 100)];
}
public class WaterWormFishingTask : MethodTask
{
    protected override string MethodName => "Water Worm Fishing";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Lost Precursor City", "Fairy Grotto", "Goblin Holdout", "Magma Fields", "Mithril Deposits"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 180)];
}
public class QuarryFishingTask : MethodTask
{
    protected override string MethodName => "Quarry Fishing";
    protected override HashSet<string> Locations => ["The Quarry", "Quarry"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class CrimsonFishingTask : MethodTask
{
    protected override string MethodName => "Crimson Fishing";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Mystic Marsh", "Volcano Cave"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("MAGMA_FISH", 150), new("ENCHANTED_MAGMAFISH", 15)];
}
public class CrimsonHotspotFishingTask : MethodTask
{
    protected override string MethodName => "Crimson Hotspot Fishing";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Mystic Marsh", "Volcano Cave"];
    protected override HashSet<string> DetectionItems => ["HOTSPOT_CATCH"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("MAGMA_FISH", 200), new("ENCHANTED_MAGMAFISH", 20)];
}
public class FestivalFishingTask : MethodTask
{
    protected override string MethodName => "Festival Fishing";
    protected override HashSet<string> Locations => ["Festival Plaza", "Jerry's Workshop"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class SquidFishingTask : MethodTask
{
    protected override string MethodName => "Squid Fishing";
    protected override HashSet<string> Locations => ["Squid Cave", "Murkwater Depths", "Murkwater Shallows", "Driptoad Delve"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("INK_SACK", 200), new("ENCHANTED_INK_SACK", 20)];
}
public class GalateaFishingMethodTask : MethodTask
{
    protected override string MethodName => "Galatea Fishing";
    protected override HashSet<string> Locations => ["Driptoad Delve", "Murkwater Depths", "Murkwater Shallows", "Squid Cave", "Reefguard Pass"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("SEA_LUMIES", 150), new("RAW_FISH", 180)];
}
public class OasisFishingTask : MethodTask
{
    protected override string MethodName => "Oasis Fishing";
    protected override HashSet<string> Locations => ["Oasis", "Mushroom Desert"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class WaterFishingTask : MethodTask
{
    protected override string MethodName => "Water Fishing";
    protected override HashSet<string> Locations => ["Hub", "Village", "Forest", "Birch Park", "The Park"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class MagmaCoreFishingTask : MethodTask
{
    protected override string MethodName => "Magma Core Fishing";
    protected override HashSet<string> Locations => ["Magma Fields", "Crystal Hollows"];
    protected override HashSet<string> DetectionItems => ["MAGMA_CORE"];
    protected override bool ExcludeShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("MAGMA_CORE", 8)];
}
public class FlamingWormFishingTask : MethodTask
{
    protected override string MethodName => "Flaming Worm Fishing";
    protected override HashSet<string> Locations => ["Magma Fields", "Crystal Hollows", "Blazing Volcano"];
    protected override HashSet<string> DetectionItems => ["FLAMING_WORM"];
    protected override List<MethodDrop> FormulaDrops => [new("FLAMING_WORM", 30)];
}

// ── Fishing with Sea Creature Hunting ──
public class PiscaryFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Piscary Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Piscary"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 250), new("ENCHANTED_RAW_FISH", 25)];
}
public class BayouFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Bayou Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Bayou Outpost", "Bayou Swamp", "The Bayou"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200), new("LILY_PAD", 80)];
}
public class BayouHotspotFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Bayou Hotspot Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Bayou Outpost", "Bayou Swamp", "The Bayou"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 300), new("LILY_PAD", 120)];
}
public class SpookyFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Spooky Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Spooky Festival", "Spooky Pier", "The Park", "Howling Cave"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200), new("PUMPKIN", 50)];
}
public class WinterFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Winter Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Jerry's Workshop", "Jerry Pond", "Hot Springs"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 220), new("ICE", 100)];
}
public class WaterWormFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Water Worm Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Lost Precursor City", "Fairy Grotto", "Goblin Holdout", "Magma Fields", "Mithril Deposits"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 180)];
}
public class QuarryFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Quarry Fishing (Hunting)";
    protected override HashSet<string> Locations => ["The Quarry", "Quarry"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class CrimsonFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Crimson Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Mystic Marsh", "Volcano Cave"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("MAGMA_FISH", 150), new("ENCHANTED_MAGMAFISH", 15)];
}
public class FestivalFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Festival Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Festival Plaza", "Jerry's Workshop"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class SquidFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Squid Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Squid Cave", "Murkwater Depths", "Murkwater Shallows", "Driptoad Delve"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("INK_SACK", 200), new("ENCHANTED_INK_SACK", 20)];
}
public class GalateaFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Galatea Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Driptoad Delve", "Murkwater Depths", "Murkwater Shallows", "Squid Cave", "Reefguard Pass"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("SEA_LUMIES", 150), new("RAW_FISH", 180)];
}
public class OasisFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Oasis Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Oasis", "Mushroom Desert"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
public class WaterFishingHuntingTask : MethodTask
{
    protected override string MethodName => "Water Fishing (Hunting)";
    protected override HashSet<string> Locations => ["Hub", "Village", "Forest", "Birch Park", "The Park"];
    protected override bool RequireShardItems => true;
    protected override List<MethodDrop> FormulaDrops => [new("RAW_FISH", 200)];
}
