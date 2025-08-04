using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class TaskParams
{
    public DateTime TestTime { get; set; }
    public PlayerState.Client.Model.ExtractedInfo ExtractedInfo { get; set; }
    public MinecraftSocket Socket { get; set; }
    public ConcurrentDictionary<Type, CalculationCache> Cache { get; set; }
    public long MaxAvailableCoins { get; set; } = 1000000000; // Default to 1 billion coins
    public Dictionary<string, Period[]> LocationProfit { get; set; }
    public Dictionary<string, long> CleanPrices { get; set; }
    public List<ItemPrice> BazaarPrices { get; set; }
    public Dictionary<string, string> Names { get;  set; }

    public T GetService<T>() where T : class
    {
        return Socket.GetService<T>();
    }

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
