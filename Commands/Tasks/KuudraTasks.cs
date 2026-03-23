using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC.Tasks;

// Kuudra tasks - all tiers share the same location, differentiation is
// difficult from location data alone. Each tier shows separately with formula
// estimates for expected per-tier profit.
public class KuudraT1Task : MethodTask
{
    protected override string MethodName => "Kuudra T1";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 600)];
    protected override string WarpCommand => "/warp kuudra";
}
public class KuudraT2Task : MethodTask
{
    protected override string MethodName => "Kuudra T2";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 900)];
    protected override string WarpCommand => "/warp kuudra";
}
public class KuudraT3Task : MethodTask
{
    protected override string MethodName => "Kuudra T3";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 1200), new("ATTRIBUTE_SHARD", 15)];
    protected override string WarpCommand => "/warp kuudra";
}
public class KuudraT4Task : MethodTask
{
    protected override string MethodName => "Kuudra T4";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 1500), new("ATTRIBUTE_SHARD", 30)];
    protected override string WarpCommand => "/warp kuudra";
}
public class KuudraT5Task : MethodTask
{
    protected override string MethodName => "Kuudra T5";
    protected override HashSet<string> Locations => ["Kuudra", "Kuudra's Hollow"];
    protected override List<MethodDrop> FormulaDrops => [new("CRIMSON_ESSENCE", 2000), new("ATTRIBUTE_SHARD", 50)];
    protected override string WarpCommand => "/warp kuudra";
}
