using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Passive task: place a hunting trap in the area with the most expensive shards.
/// The trap yields one shard after 24 hours.
/// </summary>
public class HuntingTrapTask : MethodTask
{
    // All shard types mapped to their hunting locations
    private static readonly List<(string ShardTag, string[] Locations)> ShardLocations =
    [
        ("SHARD_RAIN_SLIME", ["Spider's Den", "The Spider's Den"]),
        ("SHARD_HELLWISP", ["Blazing Volcano", "Burning Desert", "Smoldering Tomb"]),
        ("SHARD_XYZ", ["Crystal Hollows", "Precursor Remnants", "Lost Precursor City"]),
        ("SHARD_KADA_KNIGHT", ["Drowned Reliquary", "Kelpwoven Tunnels", "Reefguard Pass"]),
        ("SHARD_INVISIBUG", ["Moonglade Marsh", "North Wetlands", "South Wetlands", "Evergreen Plateau"]),
        ("SHARD_YOG", ["Magma Fields", "Blazing Volcano", "Crystal Hollows"]),
        ("SHARD_FLARE", ["Blazing Volcano", "Burning Desert", "Crimson Isle"]),
        ("SHARD_BEZAL", ["Crystal Hollows", "Precursor Remnants", "Goblin Holdout"]),
        ("SHARD_GHOST", ["Dwarven Mines", "The Mist", "Goblin Holdout"]),
        ("SHARD_FLAMING_SPIDER", ["Blazing Volcano", "Crimson Isle", "Spider's Den"]),
        ("SHARD_OBSIDIAN_DEFENDER", ["Magma Fields", "Crystal Hollows", "Precursor Remnants"]),
        ("SHARD_WITHER_SPECTER", ["The End", "Dragon's Nest", "Void Sepulture"]),
        ("SHARD_ZEALOT", ["The End", "Dragon's Nest", "Void Sepulture"]),
        ("SHARD_BRUISER", ["The End", "Dragon's Nest"]),
        ("SHARD_CINDERBAT", ["Dive-Ember Pass", "Side-Ember Way", "Stride-Ember Fissure"]),
        ("SHARD_BURNINGSOUL", ["Dive-Ember Pass", "Side-Ember Way"]),
        ("SHARD_DROWNED", ["Drowned Reliquary", "Kelpwoven Tunnels", "Murkwater Depths"]),
    ];

    protected override string MethodName => "Hunting Trap";
    protected override string Category => "Hunting";
    protected override TaskType TaskType => TaskType.Passive;
    protected override string ActionUnit => "traps";
    protected override double ActionsPerHour => 1.0 / 24; // 1 trap per 24 hours
    protected override string HowTo => "Place a hunting trap in the area with the most expensive shards. " +
        "The trap collects one shard after 24 hours. Check daily to collect and re-place.";
    protected override List<RequiredItem> RequiredItems => [
        new() { ItemTag = "HUNTING_TRAP", Reason = "Trap to place in hunting areas (1 shard per 24h)" }
    ];

    public override Task<TaskResult> Execute(TaskParams parameters)
    {
        var prices = parameters.GetPrices();
        var bestShard = ShardLocations
            .Select(s => (s.ShardTag, s.Locations, Price: prices.GetValueOrDefault(s.ShardTag, 0)))
            .Where(s => s.Price > 0)
            .OrderByDescending(s => s.Price)
            .FirstOrDefault();

        if (bestShard.ShardTag == null)
            return Task.FromResult(new TaskResult
            {
                ProfitPerHour = 0,
                Type = TaskType.Passive, MostlyPassive = true,
                Message = "Hunting Trap - no shard prices available.",
                Name = MethodName
            });

        var fmt = parameters.Formatter;
        var profitPerHour = bestShard.Price / 24.0;
        var shardName = parameters.Names.GetValueOrDefault(bestShard.ShardTag, bestShard.ShardTag);
        var location = bestShard.Locations[0];

        return Task.FromResult(new TaskResult
        {
            ProfitPerHour = (int)profitPerHour,
            Type = TaskType.Passive, MostlyPassive = true,
            IsAccessible = true,
            Message = $"Place trap at {McColorCodes.GREEN}{location} {McColorCodes.GRAY}for {McColorCodes.YELLOW}{shardName} {McColorCodes.GRAY}({fmt.FormatPrice(bestShard.Price)}/day)",
            Details = $"Best shard: {McColorCodes.YELLOW}{shardName} {McColorCodes.GRAY}worth {McColorCodes.AQUA}{fmt.FormatPrice(bestShard.Price)}\n"
                + $"Location: {McColorCodes.GREEN}{string.Join(", ", bestShard.Locations)}\n"
                + $"{McColorCodes.GRAY}Place a hunting trap there and collect after 24 hours.\n"
                + $"{McColorCodes.DARK_GRAY}(Passive — can be done alongside active tasks)",
            Name = MethodName,
            Breakdown = new MethodBreakdown
            {
                HowTo = HowTo,
                RequiredItems = RequiredItems,
                Drops = [new DropInfo
                {
                    ItemTag = bestShard.ShardTag,
                    Name = shardName,
                    RatePerHour = 1.0 / 24,
                    PriceEach = bestShard.Price,
                    ContributionPerHour = profitPerHour
                }],
                ActionsPerHour = 1.0 / 24,
                ActionUnit = "traps",
                Category = "Hunting",
                Source = "formula",
                Type = TaskType.Passive
            }
        });
    }
}
