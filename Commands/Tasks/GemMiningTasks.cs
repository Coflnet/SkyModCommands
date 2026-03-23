using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// ── Gemstone Mining ──
public class ThystMiningTask : MethodTask
{
    protected override string MethodName => "Thyst Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Mithril Deposits", "Precursor Remnants", "Magma Fields", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["FINE_THYST_GEM", "FLAWED_THYST_GEM", "ROUGH_THYST_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_THYST_GEM", 200), new("FLAWED_THYST_GEM", 400)];
}
public class JasperMiningTask : MethodTask
{
    protected override string MethodName => "Jasper Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Magma Fields"];
    protected override HashSet<string> DetectionItems => ["FINE_JASPER_GEM", "FLAWED_JASPER_GEM", "ROUGH_JASPER_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_JASPER_GEM", 180), new("FLAWED_JASPER_GEM", 360)];
}
public class JadeMiningTask : MethodTask
{
    protected override string MethodName => "Jade Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Mithril Deposits", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["FINE_JADE_GEM", "FLAWED_JADE_GEM", "ROUGH_JADE_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_JADE_GEM", 170), new("FLAWED_JADE_GEM", 340)];
}
public class AmberMiningTask : MethodTask
{
    protected override string MethodName => "Amber Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Goblin Holdout"];
    protected override HashSet<string> DetectionItems => ["FINE_AMBER_GEM", "FLAWED_AMBER_GEM", "ROUGH_AMBER_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_AMBER_GEM", 170), new("FLAWED_AMBER_GEM", 340)];
}
public class SapphireMiningTask : MethodTask
{
    protected override string MethodName => "Sapphire Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants"];
    protected override HashSet<string> DetectionItems => ["FINE_SAPPHIRE_GEM", "FLAWED_SAPPHIRE_GEM", "ROUGH_SAPPHIRE_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_SAPPHIRE_GEM", 160), new("FLAWED_SAPPHIRE_GEM", 320)];
}
public class PeridotMiningTask : MethodTask
{
    protected override string MethodName => "Peridot Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Mithril Deposits"];
    protected override HashSet<string> DetectionItems => ["FINE_PERIDOT_GEM", "FLAWED_PERIDOT_GEM", "ROUGH_PERIDOT_GEM"];
    protected override List<MethodDrop> FormulaDrops => [new("FINE_PERIDOT_GEM", 160), new("FLAWED_PERIDOT_GEM", 320)];
}

// ── Ore Mining ──
public class CoalMiningTask : MethodTask
{
    protected override string MethodName => "Coal Mining";
    protected override HashSet<string> Locations => ["Dwarven Mines", "Coal Mine", "Glacite Tunnels", "Glacite Mountains"];
    protected override HashSet<string> DetectionItems => ["COAL", "ENCHANTED_COAL"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_COAL", 500), new("ENCHANTED_COAL_BLOCK", 30)];
}
public class DiamondMiningTask : MethodTask
{
    protected override string MethodName => "Diamond Mining";
    protected override HashSet<string> Locations => ["Dwarven Mines", "Deep Caverns", "Diamond Reserve"];
    protected override HashSet<string> DetectionItems => ["DIAMOND", "ENCHANTED_DIAMOND"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_DIAMOND", 300), new("ENCHANTED_DIAMOND_BLOCK", 15)];
}
public class RedstoneMiningTask : MethodTask
{
    protected override string MethodName => "Redstone Mining";
    protected override HashSet<string> Locations => ["Dwarven Mines", "Deep Caverns", "Pigmen's Den"];
    protected override HashSet<string> DetectionItems => ["REDSTONE", "ENCHANTED_REDSTONE"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_REDSTONE", 400), new("ENCHANTED_REDSTONE_BLOCK", 20)];
}
public class CobblestoneMiningTask : MethodTask
{
    protected override string MethodName => "Cobblestone Mining";
    protected override HashSet<string> Locations => ["Gold Mine", "Deep Caverns", "Coal Mine"];
    protected override HashSet<string> DetectionItems => ["COBBLESTONE", "ENCHANTED_COBBLESTONE"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_COBBLESTONE", 600)];
}
public class ObsidianMiningTask : MethodTask
{
    protected override string MethodName => "Obsidian Mining";
    protected override HashSet<string> Locations => ["Obsidian Sanctuary", "Deep Caverns", "The End"];
    protected override HashSet<string> DetectionItems => ["OBSIDIAN", "ENCHANTED_OBSIDIAN"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_OBSIDIAN", 200)];
}
public class TungstenMiningTask : MethodTask
{
    protected override string MethodName => "Tungsten Mining";
    protected override HashSet<string> Locations => ["Glacite Tunnels", "Glacite Mountains"];
    protected override HashSet<string> DetectionItems => ["TUNGSTEN", "TUNGSTEN_ORE", "ENCHANTED_TUNGSTEN"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_TUNGSTEN", 200)];
}
public class UmberMiningTask : MethodTask
{
    protected override string MethodName => "Umber Mining";
    protected override HashSet<string> Locations => ["Glacite Tunnels", "Glacite Mountains"];
    protected override HashSet<string> DetectionItems => ["UMBER", "UMBER_ORE", "ENCHANTED_UMBER"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_UMBER", 200)];
}

// ── Special Mining ──
public class NucleusMiningTask : MethodTask
{
    protected override string MethodName => "Crystal Nucleus";
    protected override HashSet<string> Locations => ["Crystal Nucleus", "Crystal Hollows"];
    protected override HashSet<string> DetectionItems => ["CRYSTAL_FRAGMENT", "NUCLEUS_LOOT"];
    protected override List<MethodDrop> FormulaDrops => [new("CRYSTAL_FRAGMENT", 30)];
}
public class SludgeMiningTask : MethodTask
{
    protected override string MethodName => "Sludge Mining";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Forge", "Far Reserve", "Royal Mines"];
    protected override HashSet<string> DetectionItems => ["SLUDGE_JUICE", "SLUDGE"];
    protected override List<MethodDrop> FormulaDrops => [new("SLUDGE_JUICE", 300)];
}
public class SludgeMiningGemMixtureTask : MethodTask
{
    protected override string MethodName => "Sludge Mining (Gem Mixture)";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Forge", "Far Reserve", "Royal Mines"];
    protected override HashSet<string> DetectionItems => ["SLUDGE_JUICE", "GEM_MIXTURE"];
    protected override List<MethodDrop> FormulaDrops => [new("SLUDGE_JUICE", 300), new("GEM_MIXTURE", 50)];
}
public class SludgeMiningCoalTask : MethodTask
{
    protected override string MethodName => "Sludge Mining (Coal)";
    protected override HashSet<string> Locations => ["Dwarven Mines", "The Forge", "Far Reserve", "Royal Mines"];
    protected override HashSet<string> DetectionItems => ["SLUDGE_JUICE", "ENCHANTED_COAL"];
    protected override List<MethodDrop> FormulaDrops => [new("SLUDGE_JUICE", 300), new("ENCHANTED_COAL", 200)];
}
public class ScathaMiningTask : MethodTask
{
    protected override string MethodName => "Scatha Mining";
    protected override HashSet<string> Locations => ["Crystal Hollows", "Precursor Remnants", "Magma Fields"];
    protected override HashSet<string> DetectionItems => ["SCATHA_PET", "WORM_MEMBRANE"];
    protected override List<MethodDrop> FormulaDrops => [new("WORM_MEMBRANE", 20)];
}

// ── Powder Mining ──
public class PrecursorCityPowderMiningTask : MethodTask
{
    protected override string MethodName => "Precursor City Powder Mining";
    protected override HashSet<string> Locations => ["Precursor Remnants", "Lost Precursor City"];
    protected override HashSet<string> DetectionItems => ["MITHRIL_ORE", "ENCHANTED_MITHRIL"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MITHRIL", 200)];
}
public class JunglePowderMiningTask : MethodTask
{
    protected override string MethodName => "Jungle Powder Mining";
    protected override HashSet<string> Locations => ["Jungle", "Jungle Temple"];
    protected override HashSet<string> DetectionItems => ["MITHRIL_ORE", "ENCHANTED_MITHRIL", "HARD_STONE"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MITHRIL", 180), new("HARD_STONE", 500)];
}
public class MithrilDepositsPowderMiningTask : MethodTask
{
    protected override string MethodName => "Mithril Deposits Powder Mining";
    protected override HashSet<string> Locations => ["Mithril Deposits", "Dwarven Mines"];
    protected override HashSet<string> DetectionItems => ["MITHRIL_ORE", "ENCHANTED_MITHRIL"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MITHRIL", 200)];
}
public class GoblinHoldoutPowderMiningTask : MethodTask
{
    protected override string MethodName => "Goblin Holdout Powder Mining";
    protected override HashSet<string> Locations => ["Goblin Holdout", "Goblin Queen's Den"];
    protected override HashSet<string> DetectionItems => ["MITHRIL_ORE", "ENCHANTED_MITHRIL"];
    protected override List<MethodDrop> FormulaDrops => [new("ENCHANTED_MITHRIL", 180)];
}
