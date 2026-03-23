using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── Galatea mobs ──
public class CinderbatTask : MethodTask
{
    protected override string MethodName => "Cinderbat";
    protected override HashSet<string> Locations => ["Dive-Ember Pass", "Stride-Ember Fissure", "Side-Ember Way"];
    protected override HashSet<string> DetectionItems => ["SHARD_CINDERBAT"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_CINDERBAT", 300)];
}
public class BurningsoulTask : MethodTask
{
    protected override string MethodName => "Burningsoul";
    protected override HashSet<string> Locations => ["Dive-Ember Pass", "Stride-Ember Fissure", "Side-Ember Way"];
    protected override HashSet<string> DetectionItems => ["SHARD_BURNINGSOUL"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_BURNINGSOUL", 280)];
}
public class LumisquidTask : MethodTask
{
    protected override string MethodName => "Lumisquid";
    protected override HashSet<string> Locations => ["Murkwater Depths", "Murkwater Shallows", "Squid Cave"];
    protected override HashSet<string> DetectionItems => ["SHARD_LUMISQUID"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_LUMISQUID", 280)];
}
public class ShellwiseTask : MethodTask
{
    protected override string MethodName => "Shellwise";
    protected override HashSet<string> Locations => ["Reefguard Pass", "Murkwater Outpost", "South Reaches"];
    protected override HashSet<string> DetectionItems => ["SHARD_SHELLWISE"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_SHELLWISE", 270)];
}
public class MatchoTask : MethodTask
{
    protected override string MethodName => "Matcho";
    protected override HashSet<string> Locations => ["Moonglade Marsh", "North Wetlands", "South Wetlands"];
    protected override HashSet<string> DetectionItems => ["SHARD_MATCHO"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_MATCHO", 260)];
}
public class StridersurferTask : MethodTask
{
    protected override string MethodName => "Stridersurfer";
    protected override HashSet<string> Locations => ["Stride-Ember Fissure", "Side-Ember Way", "Dive-Ember Pass"];
    protected override HashSet<string> DetectionItems => ["SHARD_STRIDERSURFER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_STRIDERSURFER", 320)];
}
public class SporeTask : MethodTask
{
    protected override string MethodName => "Spore";
    protected override HashSet<string> Locations => ["Moonglade Marsh", "Wyrmgrove Tomb", "Tomb Floodway"];
    protected override HashSet<string> DetectionItems => ["SHARD_SPORE"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_SPORE", 300)];
}
public class BladesoulTask : MethodTask
{
    protected override string MethodName => "Bladesoul";
    protected override HashSet<string> Locations => ["Stronghold", "The Bastion", "Dragontail"];
    protected override HashSet<string> DetectionItems => ["SHARD_BLADESOUL"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_BLADESOUL", 200)];
}
public class JoydiveTask : MethodTask
{
    protected override string MethodName => "Joydive";
    protected override HashSet<string> Locations => ["Tranquil Pass", "Tranquility Sanctum", "Verdant Summit"];
    protected override HashSet<string> DetectionItems => ["SHARD_JOYDIVE"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_JOYDIVE", 280)];
}
public class DrownedTask : MethodTask
{
    protected override string MethodName => "Drowned";
    protected override HashSet<string> Locations => ["Drowned Reliquary", "Kelpwoven Tunnels", "Murkwater Depths"];
    protected override HashSet<string> DetectionItems => ["SHARD_DROWNED"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_DROWNED", 300)];
}
public class CoralotTask : MethodTask
{
    protected override string MethodName => "Coralot";
    protected override HashSet<string> Locations => ["Reefguard Pass", "Murkwater Outpost", "South Reaches", "Driptoad Delve"];
    protected override HashSet<string> DetectionItems => ["SHARD_CORALOT"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_CORALOT", 260)];
}
public class BambuleafTask : MethodTask
{
    protected override string MethodName => "Bambuleaf";
    protected override HashSet<string> Locations => ["North Wetlands", "South Wetlands", "Moonglade Marsh"];
    protected override HashSet<string> DetectionItems => ["SHARD_BAMBULEAF"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_BAMBULEAF", 260)];
}
public class HideonleafTask : MethodTask
{
    protected override string MethodName => "Hideonleaf";
    protected override HashSet<string> Locations => ["North Wetlands", "South Wetlands", "Evergreen Plateau"];
    protected override HashSet<string> DetectionItems => ["SHARD_HIDEONLEAF"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_HIDEONLEAF", 250)];
}
public class DreadwingTask : MethodTask
{
    protected override string MethodName => "Dreadwing";
    protected override HashSet<string> Locations => ["Wyrmgrove Tomb", "Tomb Floodway", "Ancient Ruins"];
    protected override HashSet<string> DetectionItems => ["SHARD_DREADWING"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_DREADWING", 240)];
}
public class SpikeTask : MethodTask
{
    protected override string MethodName => "Spike";
    protected override HashSet<string> Locations => ["Drowned Reliquary", "Kelpwoven Tunnels", "Murkwater Depths"];
    protected override HashSet<string> DetectionItems => ["SHARD_SPIKE"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_SPIKE", 270)];
}
public class SeerTask : MethodTask
{
    protected override string MethodName => "Seer";
    protected override HashSet<string> Locations => ["Ancient Ruins", "Tranquility Sanctum", "Verdant Summit"];
    protected override HashSet<string> DetectionItems => ["SHARD_SEER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_SEER", 240)];
}
public class MochibearkTask : MethodTask
{
    protected override string MethodName => "Mochibear";
    protected override HashSet<string> Locations => ["North Wetlands", "South Wetlands", "Moonglade Marsh", "Evergreen Plateau"];
    protected override HashSet<string> DetectionItems => ["SHARD_MOCHIBEAR"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_MOCHIBEAR", 250)];
}
public class MossybitTask : MethodTask
{
    protected override string MethodName => "Mossybit";
    protected override HashSet<string> Locations => ["North Wetlands", "South Wetlands", "Evergreen Plateau"];
    protected override HashSet<string> DetectionItems => ["SHARD_MOSSYBIT"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_MOSSYBIT", 250)];
}

// ── Non-Galatea mobs ──
public class VoraciousSpiderTask : MethodTask
{
    protected override string MethodName => "Voracious Spider";
    protected override HashSet<string> Locations => ["Spider's Den", "The Spider's Den", "Arachne's Sanctuary", "Spider Mound"];
    protected override HashSet<string> DetectionItems => ["SHARD_VORACIOUS_SPIDER", "TARANTULA_WEB"];
    protected override List<MethodDrop> FormulaDrops => [new("TARANTULA_WEB", 200), new("SPIDER_EYE", 300)];
}
public class GoldenGhoulTask : MethodTask
{
    protected override string MethodName => "Golden Ghoul";
    protected override HashSet<string> Locations => ["Ruins", "Graveyard", "Coal Mine"];
    protected override HashSet<string> DetectionItems => ["SHARD_GOLDEN_GHOUL", "GOLDEN_POWDER"];
    protected override List<MethodDrop> FormulaDrops => [new("GOLDEN_POWDER", 150)];
}
public class StarSentryTask : MethodTask
{
    protected override string MethodName => "Star Sentry";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["SHARD_STAR_SENTRY"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_STAR_SENTRY", 200)];
}
public class AutomatonTask : MethodTask
{
    protected override string MethodName => "Automaton";
    protected override HashSet<string> Locations => ["Precursor Remnants", "Lost Precursor City", "Crystal Hollows"];
    protected override HashSet<string> DetectionItems => ["AUTOMATON_PART", "SHARD_AUTOMATON"];
    protected override List<MethodDrop> FormulaDrops => [new("AUTOMATON_PART", 150)];
}
public class XyzMobTask : MethodTask
{
    protected override string MethodName => "Xyz";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Lost Precursor City"];
    protected override HashSet<string> DetectionItems => ["SHARD_XYZ"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_XYZ", 200)];
}
public class GhostMobTask : MethodTask
{
    protected override string MethodName => "Ghost";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Mist", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["GHOST_COIN", "SHARD_GHOST"];
    protected override List<MethodDrop> FormulaDrops => [new("GHOST_COIN", 400)];
}
