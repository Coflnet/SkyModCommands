using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── Base class for all hunting tasks ──
public abstract class BaseHuntingTask : MethodTask
{
    protected override string Category => "Hunting";
    protected override string ActionUnit => "kills";
    protected override List<DropEffect> Effects =>
    [
        new() { Name = "Magic Find", Description = "Increases chance of rare shard drops", EstimatedMultiplier = 1.3 },
        new() { Name = "Looting", Description = "Higher looting enchantment increases shard drop rate", EstimatedMultiplier = 1.2 },
        new() { Name = "Combat Level", Description = "Higher combat level increases kill speed", EstimatedMultiplier = 1.1 }
    ];
    protected override List<RequiredItem> RequiredItems => [
        new() { ItemTag = "ASPECT_OF_THE_DRAGONS", Reason = "Weapon for mob hunting" }
    ];
}

// ── Hunting methods (dedicated mob hunting, non-fishing) ──

public class RainSlimeHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Rain Slime (Hunting)";
    protected override HashSet<string> Locations => ["Spider's Den", "The Spider's Den"];
    protected override HashSet<string> DetectionItems => ["SHARD_RAIN_SLIME", "RAIN_SLIME"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_RAIN_SLIME", 200)];
    protected override string HowTo => "Go to Spider's Den and hunt Rain Slimes. They spawn during rain events (first 20 minutes of each hour). Use a weapon with high damage.";
    /// <summary>
    /// Rain Slimes only spawn from :00 to :20 each hour
    /// </summary>
    protected override string CheckAccessibility(TaskParams parameters)
    {
        var minute = parameters.TestTime.Minute;
        if (minute >= 20)
            return $"Rain Slimes only spawn from :00 to :20 each hour. Available again in {60 - minute + 0} minutes.";
        return base.CheckAccessibility(parameters);
    }
}
public class HellwispHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Hellwisp (Hunting)";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Smoldering Tomb"];
    protected override HashSet<string> DetectionItems => ["SHARD_HELLWISP"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_HELLWISP", 200)];
    protected override string HowTo => "Go to Blazing Volcano or Burning Desert on the Crimson Isle and hunt Hellwisps. They are fire mobs in lava areas.";
}
public class XyzHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Xyz (Hunting)";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Lost Precursor City"];
    protected override HashSet<string> DetectionItems => ["SHARD_XYZ"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_XYZ", 250)];
    protected override string HowTo => "Go to the Crystal Hollows (Precursor Remnants area) and hunt Xyz mobs. Use a strong weapon as they have high HP.";
}
public class KadaKnightHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Kada Knight (Hunting)";
    protected override HashSet<string> Locations => ["Drowned Reliquary", "Kelpwoven Tunnels", "Reefguard Pass"];
    protected override HashSet<string> DetectionItems => ["SHARD_KADA_KNIGHT"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_KADA_KNIGHT", 230)];
    protected override string HowTo => "Go to the underwater areas on Galatea and hunt Kada Knights. They spawn in the Drowned Reliquary and Kelpwoven Tunnels.";
}
public class InvisibugHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Invisibug (Hunting)";
    protected override HashSet<string> Locations => ["Moonglade Marsh", "North Wetlands", "South Wetlands", "Evergreen Plateau"];
    protected override HashSet<string> DetectionItems => ["SHARD_INVISIBUG"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_INVISIBUG", 220)];
    protected override string HowTo => "Go to the marsh/wetland areas on Galatea and hunt Invisibugs. They are invisible until attacked, use AoE weapons.";
}
public class YogHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Yog (Hunting)";
    protected override HashSet<string> Locations => ["Magma Fields", "Blazing Volcano", "Crystal Hollows"];
    protected override HashSet<string> DetectionItems => ["SHARD_YOG"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_YOG", 250)];
    protected override string HowTo => "Go to Magma Fields or Blazing Volcano and hunt Yogs. They are lava mobs that drop valuable shards.";
}
public class FlareHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Flare (Hunting)";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Burning Desert", "Crimson Isle"];
    protected override HashSet<string> DetectionItems => ["SHARD_FLARE"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_FLARE", 200)];
    protected override string HowTo => "Go to Blazing Volcano or Burning Desert and hunt Flares. They are fire-based mobs on the Crimson Isle.";
}
public class BezalHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Bezal (Hunting)";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["SHARD_BEZAL"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_BEZAL", 220)];
    protected override string HowTo => "Go to the Crystal Hollows and hunt Bezals in the Precursor Remnants or Goblin Holdout areas.";
}
public class GhostHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Ghost (Hunting)";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Mist", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["GHOST_COIN", "SHARD_GHOST"];
    protected override List<MethodDrop> FormulaDrops => [new("GHOST_COIN", 500)];
    protected override string HowTo => "Go to The Mist in the Dwarven Mines and hunt Ghosts. They drop Ghost Coins. Use a weapon with high damage and Magic Find gear.";
}
public class FlamingSpiderHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Flaming Spider (Hunting)";
    protected override HashSet<string> Locations => ["Blazing Volcano", "Crimson Isle", "Spider's Den"];
    protected override HashSet<string> DetectionItems => ["SHARD_FLAMING_SPIDER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_FLAMING_SPIDER", 200)];
    protected override string HowTo => "Go to Blazing Volcano or Crimson Isle and hunt Flaming Spiders. They are fire-type spider mobs.";
}
public class ObsidianDefenderHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Obsidian Defender (Hunting)";
    protected override HashSet<string> Locations => ["Magma Fields", "Crystal Hollows", "Precursor Remnants"];
    protected override HashSet<string> DetectionItems => ["SHARD_OBSIDIAN_DEFENDER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_OBSIDIAN_DEFENDER", 180)];
    protected override string HowTo => "Go to Magma Fields or Crystal Hollows and hunt Obsidian Defenders. They are tanky mobs that drop valuable shards.";
}
public class WitherSpecterHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Wither Specter (Hunting)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["SHARD_WITHER_SPECTER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_WITHER_SPECTER", 200)];
    protected override string HowTo => "Go to The End and hunt Wither Specters. They spawn in the Dragon's Nest and Void Sepulture areas.";
}
public class ZealotHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Zealot (Hunting)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest", "Void Sepulture"];
    protected override HashSet<string> DetectionItems => ["SHARD_ZEALOT", "SUMMONING_EYE"];
    protected override List<MethodDrop> FormulaDrops => [new("SUMMONING_EYE", 3)];
    protected override string HowTo => "Go to The End and hunt Zealots for Summoning Eyes. Kill Special Zealots for guaranteed eye drops. Fast kill speed is key.";
}
public class BruiserHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Bruiser (Hunting)";
    protected override HashSet<string> Locations => ["The End", "Dragon's Nest"];
    protected override HashSet<string> DetectionItems => ["SHARD_BRUISER"];
    protected override List<MethodDrop> FormulaDrops => [new("SHARD_BRUISER", 200)];
    protected override string HowTo => "Go to The End and hunt Bruisers. They are tank-type mobs in the Dragon's Nest area.";
}
public class PestHuntingTask : BaseHuntingTask
{
    protected override string MethodName => "Pest (Hunting)";
    protected override HashSet<string> Locations => ["The Garden", "Plot 1", "Plot 2", "Plot 3", "Plot 4", "Plot 5", "Plot 6", "Plot 7", "Plot 8", "Plot 9", "Plot 10", "Plot 11", "Plot 12"];
    protected override HashSet<string> DetectionItems => ["PEST_KILL", "ENCHANTED_CROP"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_CROP", 100)];
    protected override string HowTo => "Go to The Garden and hunt Pests that spawn on your plots. Use the Pesterminator vacuum or manual combat.";
    protected override string Category => "Garden";
}
