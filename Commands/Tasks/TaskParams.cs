using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class TaskParams
{
    public DateTime TestTime { get; set; }
    public PlayerState.Client.Model.ExtractedInfo ExtractedInfo { get; set; }
    public MinecraftSocket Socket { get; set; }
    /// <summary>
    /// Format provider for prices and times. Use this instead of Socket for formatting.
    /// </summary>
    public ITaskFormatProvider Formatter { get; set; }
    public ConcurrentDictionary<Type, CalculationCache> Cache { get; set; }
    public long MaxAvailableCoins { get; set; } = 1000000000; // Default to 1 billion coins
    public Dictionary<string, Period[]> LocationProfit { get; set; }
    public Dictionary<string, long> CleanPrices { get; set; }
    public List<ItemPrice> BazaarPrices { get; set; }
    public Dictionary<string, string> Names { get;  set; }
    /// <summary>
    /// Current mayor name (lowercase), used for accessibility checks on mayor-dependent tasks.
    /// Null if unknown.
    /// </summary>
    public string CurrentMayor { get; set; }

    /// <summary>
    /// Community-aggregated average drop rates per method name.
    /// Used as a middle tier between player-specific data and static formulas.
    /// Key = method name, Value = aggregated drops per hour from all users.
    /// </summary>
    public Dictionary<string, List<AverageDrop>> GlobalAverageDrops { get; set; }

    /// <summary>
    /// DI service provider for the REST API path (where Socket is null).
    /// </summary>
    public IServiceProvider ServiceProvider { get; set; }

    /// <summary>
    /// Player UUID, available in both WebSocket and REST paths.
    /// </summary>
    public string PlayerUuid { get; set; }

    /// <summary>
    /// Player name, may be same as PlayerUuid when not known.
    /// </summary>
    public string PlayerName { get; set; }

    public T GetService<T>() where T : class
    {
        if (ServiceProvider != null)
            return ServiceProvider.GetService<T>();
        return Socket?.GetService<T>();
    }

    /// <summary>
    /// Shard counts from player state (e.g. attribute shards collected)
    /// </summary>
    public Dictionary<string, int> Shards => ExtractedInfo?.ShardCounts ?? new();

    /// <summary>
    /// Attribute/stat levels from player state
    /// </summary>
    public Dictionary<string, int> Stats => ExtractedInfo?.AttributeLevel ?? new();

    public Dictionary<string, float> GetPrices()
    {
        var combined = new Dictionary<string, float>();
        foreach (var price in CleanPrices)
        {
            combined[price.Key] = price.Value;
        }
        foreach (var itemPrice in BazaarPrices)
        {
            if (!combined.ContainsKey(itemPrice.ProductId))
            {
                combined[itemPrice.ProductId] = (float)itemPrice.BuyPrice;
            }
        }
        return combined;
    }

    public class CalculationCache
    {
        public object Data { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}

/// <summary>
/// Aggregated average drop rate from community data for a specific item in a specific method.
/// </summary>
public record AverageDrop(string ItemTag, double RatePerHour, int SampleCount);
