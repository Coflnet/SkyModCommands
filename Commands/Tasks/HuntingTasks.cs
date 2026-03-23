using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── Hunting methods (dedicated mob hunting, non-fishing) ──

public class RainSlimeHuntingTask : MethodTask
{
    protected override string MethodName => "Rain Slime (Hunting)";
    protected override HashSet<string> Locations => ["Spider's Den", "The Spider's Den"];
    protected override HashSet<string> DetectionItems => ["SHARD_RAIN_SLIME", "RAIN_SLIME"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_RAIN_SLIME", 200)];
}
public class HellwispHuntingTask : MethodTask
{
    protected override string MethodName => "Hellwisp (Hunting)";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Smoldering Tomb"];
    protected override HashSet<string> DetectionItems => ["SHARD_HELLWISP"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_HELLWISP", 200)];
}
public class XyzHuntingTask : MethodTask
{
    protected override string MethodName => "Xyz (Hunting)";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Lost Precursor City"];
    protected override HashSet<string> DetectionItems => ["SHARD_XYZ"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_XYZ", 250)];
}
public class KadaKnightHuntingTask : MethodTask
{
    protected override string MethodName => "Kada Knight (Hunting)";
    protected override HashSet<string> Locations => ["Drowned Reliquary", "Kelpwoven Tunnels", "Reefguard Pass"];
    protected override HashSet<string> DetectionItems => ["SHARD_KADA_KNIGHT"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_KADA_KNIGHT", 230)];
}
public class InvisibugHuntingTask : MethodTask
{
    protected override string MethodName => "Invisibug (Hunting)";
    protected override HashSet<string> Locations => ["Moonglade Marsh", "North Wetlands", "South Wetlands", "Evergreen Plateau"];
    protected override HashSet<string> DetectionItems => ["SHARD_INVISIBUG"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_INVISIBUG", 220)];
}
public class YogHuntingTask : MethodTask
{
    protected override string MethodName => "Yog (Hunting)";
    protected override HashSet<string> Locations => ["Magma Fields", "Blazing Volcano", "Crystal Hollows"];
    protected override HashSet<string> DetectionItems => ["SHARD_YOG"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_YOG", 250)];
}
public class FlareHuntingTask : MethodTask
{
    protected override string MethodName => "Flare (Hunting)";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Crimson Isle"];
    protected override HashSet<string> DetectionItems => ["SHARD_FLARE"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_FLARE", 200)];
}
public class BezalHuntingTask : MethodTask
{
    protected override string MethodName => "Bezal (Hunting)";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["SHARD_BEZAL"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_BEZAL", 220)];
}
public class GhostHuntingTask : MethodTask
{
    protected override string MethodName => "Ghost (Hunting)";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Mist", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["GHOST_COIN", "SHARD_GHOST"];
    protected override List<MethodDrop> FormulaDrops => [new("GHOST_COIN", 500)];
}
public class FlamingSpiderHuntingTask : MethodTask
{
    protected override string MethodName => "Flaming Spider (Hunting)";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Crimson Isle", "Spider's Den"];
    protected override HashSet<string> DetectionItems => ["SHARD_FLAMING_SPIDER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_FLAMING_SPIDER", 200)];
}
public class ObsidianDefenderHuntingTask : MethodTask
{
    protected override string MethodName => "Obsidian Defender (Hunting)";
    protected override HashSet<string> Locations => ["Magma Fields", "Crystal Hollows", "Precursor Remnants"];
    protected override HashSet<string> DetectionItems => ["SHARD_OBSIDIAN_DEFENDER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_OBSIDIAN_DEFENDER", 180)];
}
public class WitherSpecterHuntingTask : MethodTask
{
    protected override string MethodName => "Wither Specter (Hunting)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["SHARD_WITHER_SPECTER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_WITHER_SPECTER", 200)];
}
public class ZealotHuntingTask : MethodTask
{
    protected override string MethodName => "Zealot (Hunting)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["SHARD_ZEALOT", "SUMMONING_EYE"];
    protected override List<MethodDrop> FormulaDrops => [new("SUMMONING_EYE", 3)];
}
public class BruiserHuntingTask : MethodTask
{
    protected override string MethodName => "Bruiser (Hunting)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest"];
    protected override HashSet<string> DetectionItems => ["SHARD_BRUISER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_BRUISER", 200)];
}
public class PestHuntingTask : MethodTask
{
    protected override string MethodName => "Pest (Hunting)";
    protected override HashSet<string> Locations => ["The Garden", "Plot 1", "Plot 2", "Plot 3", "Plot 4", "Plot 5", "Plot 6", "Plot 7", "Plot 8", "Plot 9", "Plot 10", "Plot 11", "Plot 12"];
    protected override HashSet<string> DetectionItems => ["PEST_KILL", "ENCHANTED_CROP"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_CROP", 100)];
}
