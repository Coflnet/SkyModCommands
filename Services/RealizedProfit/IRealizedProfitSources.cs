using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>Loads the realized coins per source for all accounts of one user, each call may fail independently.</summary>
public interface IRealizedProfitSources
{
    Task<long> GetAhProfit(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token);
    Task<long> GetBazaarProfit(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token);
    Task<long> GetTaskValue(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token);
}

public interface IRealizedProfitStore
{
    Task Upsert(RealizedProfitRow row);
    Task<List<RealizedProfitRow>> GetTier(int tier);
}
