using System;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC.Tasks;

public abstract class ProfitTask
{
    public abstract Task<TaskResult> Execute(TaskParams parameters);
    public abstract string Description { get; }

    protected async Task<T> GetOrUpdateCache<T>(TaskParams parameters, Func<Task<T>> factory, float maxageMinutes = 5) where T : new()
    {
        if (parameters.Cache.TryGetValue(typeof(T), out var cache) && cache.LastUpdated > DateTime.UtcNow.AddMinutes(-maxageMinutes))
        {
            return (T)cache.Data;
        }
        var newData = await factory();
        parameters.Cache[typeof(T)] = new TaskParams.CalculationCache { Data = newData, LastUpdated = DateTime.UtcNow };
        return newData;
    }
}
