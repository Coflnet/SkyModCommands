using System;
using System.Collections.Concurrent;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class TaskParams
{
    public DateTime TestTime { get; set; }
    public PlayerState.Client.Model.ExtractedInfo ExtractedInfo { get; set; }
    public MinecraftSocket Socket { get; set; }
    public ConcurrentDictionary<Type, CalculationCache> Cache { get; set; }
    public long MaxAvailableCoins { get; set; } = 1000000000; // Default to 1 billion coins
    public T GetService<T>() where T : class
    {
        return Socket.GetService<T>();
    }

    public class CalculationCache
    {
        public object Data { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
