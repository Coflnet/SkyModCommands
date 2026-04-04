using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── Base Mining Task ──
public abstract class BaseMiningTask : MethodTask
{
    protected override string Category => "Mining";
    protected override string ActionUnit => "mines";
    protected override List<RequiredItem> RequiredItems => [new() { ItemTag = "GEMSTONE_GAUNTLET", Reason = "Mining tool" }];
    protected override List<DropEffect> Effects => [
        new() { Name = "Mining Speed", Description = "Faster block breaking", EstimatedMultiplier = 1.3 },
        new() { Name = "Mining Fortune", Description = "More drops per block", EstimatedMultiplier = 1.5 },
        new() { Name = "Pristine", Description = "Chance to upgrade gem quality on mine", EstimatedMultiplier = 1.2 }
    ];
}

// ── Gemstone Mining ──
public class ThystMiningTask : BaseMiningTask
{
    protected override string MethodName => "Thyst Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Mithril Deposits", "Precursor Remnants", "Magma Fields", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["FINE_THYST_GEM", "FLAWED_THYST_GEM", "ROUGH_THYST_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_THYST_GEM", 200), new("FLAWED_THYST_GEM", 400)];
    protected override string HowTo => "Equip a Gemstone Gauntlet and mine Thyst gemstone veins in the Crystal Hollows.";
    protected override double ActionsPerHour => 3200;
}
public class JasperMiningTask : BaseMiningTask
{
    protected override string MethodName => "Jasper Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Magma Fields"];
    protected override HashSet<string> DetectionItems => ["FINE_JASPER_GEM", "FLAWED_JASPER_GEM", "ROUGH_JASPER_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_JASPER_GEM", 180), new("FLAWED_JASPER_GEM", 360)];
    protected override string HowTo => "Equip a Gemstone Gauntlet and mine Jasper gemstone veins in the Crystal Hollows.";
    protected override double ActionsPerHour => 3000;
}
public class JadeMiningTask : BaseMiningTask
{
    protected override string MethodName => "Jade Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Mithril Deposits", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["FINE_JADE_GEM", "FLAWED_JADE_GEM", "ROUGH_JADE_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_JADE_GEM", 170), new("FLAWED_JADE_GEM", 340)];
    protected override string HowTo => "Equip a Gemstone Gauntlet and mine Jade gemstone veins in the Crystal Hollows.";
    protected override double ActionsPerHour => 2900;
}
public class AmberMiningTask : BaseMiningTask
{
    protected override string MethodName => "Amber Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["FINE_AMBER_GEM", "FLAWED_AMBER_GEM", "ROUGH_AMBER_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_AMBER_GEM", 170), new("FLAWED_AMBER_GEM", 340)];
    protected override string HowTo => "Equip a Gemstone Gauntlet and mine Amber gemstone veins in the Crystal Hollows.";
    protected override double ActionsPerHour => 2900;
}
public class SapphireMiningTask : BaseMiningTask
{
    protected override string MethodName => "Sapphire Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants"];
    protected override HashSet<string> DetectionItems => ["FINE_SAPPHIRE_GEM", "FLAWED_SAPPHIRE_GEM", "ROUGH_SAPPHIRE_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_SAPPHIRE_GEM", 160), new("FLAWED_SAPPHIRE_GEM", 320)];
    protected override string HowTo => "Equip a Gemstone Gauntlet and mine Sapphire gemstone veins in the Crystal Hollows.";
    protected override double ActionsPerHour => 2800;
}
public class PeridotMiningTask : BaseMiningTask
{
    protected override string MethodName => "Peridot Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Mithril Deposits"];
    protected override HashSet<string> DetectionItems => ["FINE_PERIDOT_GEM", "FLAWED_PERIDOT_GEM", "ROUGH_PERIDOT_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_PERIDOT_GEM", 160), new("FLAWED_PERIDOT_GEM", 320)];
    protected override string HowTo => "Equip a Gemstone Gauntlet and mine Peridot gemstone veins in the Crystal Hollows.";
    protected override double ActionsPerHour => 2800;
}

// ── Ore Mining ──
public class CoalMiningTask : BaseMiningTask
{
    protected override string MethodName => "Coal Mining";
    protected override HashSet<string> Locations => ["Dwarven Mines", "Coal Mine", "Glacite Tunnels", "Glacite Mountains"];
    protected override HashSet<string> DetectionItems => ["COAL", "ENCHANTED_COAL"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_COAL", 500), new("ENCHANTED_COAL_BLOCK", 30)];
    protected override string HowTo => "Mine coal ore blocks in the Dwarven Mines or Glacite Tunnels with a pickaxe.";
    protected override double ActionsPerHour => 4000;
}
public class DiamondMiningTask : BaseMiningTask
{
    protected override string MethodName => "Diamond Mining";
    protected override HashSet<string> Locations => ["Dwarven Mines", "Deep Caverns", "Diamond Reserve"];
    protected override HashSet<string> DetectionItems => ["DIAMOND", "ENCHANTED_DIAMOND"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_DIAMOND", 300), new("ENCHANTED_DIAMOND_BLOCK", 15)];
    protected override string HowTo => "Mine diamond ore blocks in the Deep Caverns or Diamond Reserve with a pickaxe.";
    protected override double ActionsPerHour => 3500;
}
public class RedstoneMiningTask : BaseMiningTask
{
    protected override string MethodName => "Redstone Mining";
    protected override HashSet<string> Locations => ["Dwarven Mines", "Deep Caverns", "Pigmen's Den"];
    protected override HashSet<string> DetectionItems => ["REDSTONE", "ENCHANTED_REDSTONE"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_REDSTONE", 400), new("ENCHANTED_REDSTONE_BLOCK", 20)];
    protected override string HowTo => "Mine redstone ore blocks in the Deep Caverns or Pigmen's Den with a pickaxe.";
    protected override double ActionsPerHour => 3800;
}
public class CobblestoneMiningTask : BaseMiningTask
{
    protected override string MethodName => "Cobblestone Mining";
    protected override HashSet<string> Locations => ["Gold Mine", "Deep Caverns", "Coal Mine"];
    protected override HashSet<string> DetectionItems => ["COBBLESTONE", "ENCHANTED_COBBLESTONE"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_COBBLESTONE", 600)];
    protected override string HowTo => "Mine cobblestone in the Gold Mine or Coal Mine with a pickaxe.";
    protected override double ActionsPerHour => 5000;
}
public class ObsidianMiningTask : BaseMiningTask
{
    protected override string MethodName => "Obsidian Mining";
    protected override HashSet<string> Locations => ["Obsidian Sanctuary", "Deep Caverns", "The End"];
    protected override HashSet<string> DetectionItems => ["OBSIDIAN", "ENCHANTED_OBSIDIAN"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_OBSIDIAN", 200)];
    protected override string HowTo => "Mine obsidian blocks in the Obsidian Sanctuary or The End with a pickaxe.";
    protected override double ActionsPerHour => 2000;
}
public class TungstenMiningTask : BaseMiningTask
{
    protected override string MethodName => "Tungsten Mining";
    protected override HashSet<string> Locations => ["Glacite Tunnels", "Glacite Mountains"];
    protected override HashSet<string> DetectionItems => ["TUNGSTEN", "TUNGSTEN_ORE", "ENCHANTED_TUNGSTEN"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_TUNGSTEN", 200)];
    protected override string HowTo => "Mine tungsten ore in the Glacite Tunnels or Glacite Mountains with a pickaxe.";
    protected override double ActionsPerHour => 2500;
}
public class UmberMiningTask : BaseMiningTask
{
    protected override string MethodName => "Umber Mining";
    protected override HashSet<string> Locations => ["Glacite Tunnels", "Glacite Mountains"];
    protected override HashSet<string> DetectionItems => ["UMBER", "UMBER_ORE", "ENCHANTED_UMBER"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_UMBER", 200)];
    protected override string HowTo => "Mine umber ore in the Glacite Tunnels or Glacite Mountains with a pickaxe.";
    protected override double ActionsPerHour => 2500;
}

// ── Special Mining ──
public class NucleusMiningTask : BaseMiningTask
{
    protected override string MethodName => "Crystal Nucleus";
    protected override HashSet<string> Locations => ["Crystal Nucleus", "Crystal Hollows"];
    protected override HashSet<string> DetectionItems => ["CRYSTAL_FRAGMENT", "NUCLEUS_LOOT"];
    protected override List<MethodDrop> FormulaDrops => [new("CRYSTAL_FRAGMENT", 30)];
    protected override string HowTo => "Collect crystal fragments and complete Nucleus runs in the Crystal Hollows.";
    protected override double ActionsPerHour => 600;
}
public class SludgeMiningTask : BaseMiningTask
{
    protected override string MethodName => "Sludge Mining";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Forge", "Far Reserve", "Royal Mines"];
    protected override HashSet<string> DetectionItems => ["SLUDGE_JUICE", "SLUDGE"];
    protected override List<MethodDrop> FormulaDrops => [new("SLUDGE_JUICE", 300)];
    protected override string HowTo => "Mine sludge in the Dwarven Mines or Royal Mines with a pickaxe.";
    protected override double ActionsPerHour => 3000;
}
public class SludgeMiningGemMixtureTask : BaseMiningTask
{
    protected override string MethodName => "Sludge Mining (Gem Mixture)";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Forge", "Far Reserve", "Royal Mines"];
    protected override HashSet<string> DetectionItems => ["SLUDGE_JUICE", "GEM_MIXTURE"];
    protected override List<MethodDrop> FormulaDrops => [new("SLUDGE_JUICE", 300), new("GEM_MIXTURE", 50)];
    protected override string HowTo => "Mine sludge in the Dwarven Mines to collect Sludge Juice and Gem Mixture drops.";
    protected override double ActionsPerHour => 3000;
}
public class SludgeMiningCoalTask : BaseMiningTask
{
    protected override string MethodName => "Sludge Mining (Coal)";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Forge", "Far Reserve", "Royal Mines"];
    protected override HashSet<string> DetectionItems => ["SLUDGE_JUICE", "ENCHANTED_COAL"];
    protected override List<MethodDrop> FormulaDrops => [new("SLUDGE_JUICE", 300), new("ENCHANTED_COAL", 200)];
    protected override string HowTo => "Mine sludge in the Dwarven Mines to collect Sludge Juice and Coal drops.";
    protected override double ActionsPerHour => 3000;
}
public class ScathaMiningTask : BaseMiningTask
{
    protected override string MethodName => "Scatha Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Magma Fields"];
    protected override HashSet<string> DetectionItems => ["SCATHA_PET", "WORM_MEMBRANE"];
    protected override List<MethodDrop> FormulaDrops => [new("WORM_MEMBRANE", 20)];
    protected override string HowTo => "Mine in the Crystal Hollows to spawn and kill Scatha worms for rare drops.";
    protected override double ActionsPerHour => 1500;
}

// ── Powder Mining ──
public class PrecursorCityPowderMiningTask : BaseMiningTask
{
    protected override string MethodName => "Precursor City Powder Mining";
    protected override HashSet<string> Locations => ["Precursor Remnants", "Lost Precursor City"];
    protected override HashSet<string> DetectionItems => ["MITHRIL_ORE", "ENCHANTED_MITHRIL"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MITHRIL", 200)];
    protected override string HowTo => "Mine mithril and hardstone in the Precursor City area for powder and mithril drops.";
    protected override double ActionsPerHour => 3500;
}
public class JunglePowderMiningTask : BaseMiningTask
{
    protected override string MethodName => "Jungle Powder Mining";
    protected override HashSet<string> Locations => ["Jungle", "Jungle Temple"];
    protected override HashSet<string> DetectionItems => ["MITHRIL_ORE", "ENCHANTED_MITHRIL", "HARD_STONE"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MITHRIL", 180), new("HARD_STONE", 500)];
    protected override string HowTo => "Mine mithril and hardstone in the Jungle area for powder and mithril drops.";
    protected override double ActionsPerHour => 3200;
}
public class MithrilDepositsPowderMiningTask : BaseMiningTask
{
    protected override string MethodName => "Mithril Deposits Powder Mining";
    protected override HashSet<string> Locations => ["Mithril Deposits", "Dwarven Mines"];
    protected override HashSet<string> DetectionItems => ["MITHRIL_ORE", "ENCHANTED_MITHRIL"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MITHRIL", 200)];
    protected override string HowTo => "Mine mithril ore in the Mithril Deposits for powder and mithril drops.";
    protected override double ActionsPerHour => 3500;
}
public class GoblinHoldoutPowderMiningTask : BaseMiningTask
{
    protected override string MethodName => "Goblin Holdout Powder Mining";
    protected override HashSet<string> Locations => ["Goblin Holdout", "Goblin Queen's Den"];
    protected override HashSet<string> DetectionItems => ["MITHRIL_ORE", "ENCHANTED_MITHRIL"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MITHRIL", 180)];
    protected override string HowTo => "Mine mithril ore in the Goblin Holdout for powder and mithril drops.";
    protected override double ActionsPerHour => 3200;
}
