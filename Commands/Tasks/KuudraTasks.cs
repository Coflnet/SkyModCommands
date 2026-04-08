using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

public abstract class BaseKuudraTask : MethodTask
{
    protected override string Category => "Kuudra";
    protected override string ActionUnit => "runs";
    protected override string WarpCommand => "/warp kuudra";
    protected override List<RequiredItem> RequiredItems => [
        new() { ItemTag = "TERROR_CHESTPLATE", Reason = "Kuudra armor set" },
        new() { ItemTag = "INFERNAL_CRIMSON_DAGGER", Reason = "Weapon" }
    ];
    protected override List<DropEffect> Effects => [
        new() { Name = "Crimson Essence multiplier", Description = "Higher tier drops more essence per run", EstimatedMultiplier = 1.0 },
        new() { Name = "Party size", Description = "Full party of 4 speeds up runs significantly", EstimatedMultiplier = 1.5 }
    ];
}

public class KuudraT1Task : BaseKuudraTask
{
    protected override string MethodName => "Kuudra T1";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 600)];
    protected override double ActionsPerHour => 12;
    protected override string HowTo => "Queue for Kuudra Basic (T1) via the NPC in the Crimson Isle. Fight waves of mobs and defeat Kuudra. Easiest tier, good for beginners.";
}
public class KuudraT2Task : BaseKuudraTask
{
    protected override string MethodName => "Kuudra T2";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 900)];
    protected override double ActionsPerHour => 10;
    protected override string HowTo => "Queue for Kuudra Hot (T2). Requires better gear than T1, drops more Crimson Essence.";
}
public class KuudraT3Task : BaseKuudraTask
{
    protected override string MethodName => "Kuudra T3";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 1200), new("ATTRIBUTE_SHARD", 15)];
    protected override double ActionsPerHour => 8;
    protected override string HowTo => "Queue for Kuudra Burning (T3). First tier that drops Attribute Shards. Requires good armor and team coordination.";
}
public class KuudraT4Task : BaseKuudraTask
{
    protected override string MethodName => "Kuudra T4";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 1500), new("ATTRIBUTE_SHARD", 30)];
    protected override double ActionsPerHour => 6;
    protected override string HowTo => "Queue for Kuudra Fiery (T4). High attribute shard drops. Requires strong Terror/Aurora armor and good team.";
}
public class KuudraT5Task : BaseKuudraTask
{
    protected override string MethodName => "Kuudra T5";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 2000), new("ATTRIBUTE_SHARD", 50)];
    protected override double ActionsPerHour => 5;
    protected override string HowTo => "Queue for Kuudra Infernal (T5). Highest tier with best drops. Requires maxed gear, team of 4 experienced players. Best money maker in the game for endgame players.";
}
