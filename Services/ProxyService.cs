using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Services;

public class ProxyService
{
    private readonly ISession session;
    private readonly IConfiguration config;
    private readonly ILogger<ProxyService> logger;
    private readonly Dictionary<string, Queue<MinecraftSocket>> availableSockets = new();
    private readonly Dictionary<string, MinecraftSocket> socketsByUserId = new();
    private readonly object lockObject = new();

    public ProxyService(ISession session, IConfiguration config, ILogger<ProxyService> logger)
    {
        this.session = session;
        this.config = config;
        this.logger = logger;

        // Create table if not exists
        InitializeTable();
    }

    private void InitializeTable()
    {
        try
        {
            // Use the Table<T> mapping to create tables if they do not exist.
            // This keeps table creation consistent with other services in the repo
            // and uses the mapping defined by ProxyResponseTable/ProxyCounterTable.
            var table = GetTable();
            table.CreateIfNotExists();

            var counterTable = GetCounterTable();
            counterTable.CreateIfNotExists();

            // Ensure proxy_responses keeps the desired default TTL (previously set with raw CQL)
            try
            {
                var ks = session.Keyspace;
                if (!string.IsNullOrEmpty(ks))
                {
                    // Query system schema to check current default_time_to_live
                    try
                    {
                        var rows = session.Execute($"SELECT default_time_to_live FROM system_schema.tables WHERE keyspace_name = '{ks}' AND table_name = 'proxy_responses'");
                        var row = rows.FirstOrDefault();
                        var current = row != null ? row.GetValue<int?>("default_time_to_live") ?? 0 : 0;
                        if (current != 3600)
                        {
                            // Use TimeWindowCompactionStrategy to avoid large rewrite compactions for TTL-heavy time-series data.
                            session.Execute("ALTER TABLE " + ks + ".proxy_responses WITH compaction = {'class': 'TimeWindowCompactionStrategy', 'compaction_window_unit':'HOURS', 'compaction_window_size':'1'} AND default_time_to_live = 3600;");
                        }
                    }
                    catch (Exception)
                    {
                        // If querying system_schema fails for any reason, attempt to ALTER anyway as a best-effort.
                        try
                        {
                            session.Execute("ALTER TABLE " + ks + ".proxy_responses WITH compaction = {'class': 'TimeWindowCompactionStrategy', 'compaction_window_unit':'HOURS', 'compaction_window_size':'1'} AND default_time_to_live = 3600;");
                        }
                        catch (Exception exAlterFallback)
                        {
                            logger.LogWarning(exAlterFallback, "Failed to set default_time_to_live for proxy_responses table (fallback)");
                        }
                    }
                }
                else
                {
                    // No keyspace on the session, do a best-effort alter by unqualified table name.
                    // No keyspace on session: best-effort unqualified ALTER. Prefer setting the session keyspace in production.
                    session.Execute("ALTER TABLE proxy_responses WITH compaction = {'class': 'TimeWindowCompactionStrategy', 'compaction_window_unit':'HOURS', 'compaction_window_size':'1'} AND default_time_to_live = 3600;");
                }
            }
            catch (Exception exAlter)
            {
                logger.LogWarning(exAlter, "Failed to set default_time_to_live for proxy_responses table");
            }

            logger.LogInformation("Proxy responses and counters tables initialized");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize proxy tables");
        }
    }

    public Table<ProxyResponseTable> GetTable()
    {
        return new Table<ProxyResponseTable>(session);
    }

    public Table<ProxyCounterTable> GetCounterTable()
    {
        // Use default mapping configuration to map ProxyCounterTable to proxy_counters
        return new Table<ProxyCounterTable>(session);
    }

    public void RegisterSocket(MinecraftSocket socket)
    {
        var acct = socket.sessionLifesycle?.AccountInfo?.Value;
        if (acct == null)
            return;

        if (!acct.ProxyOptIn)
            return;

        var locale = acct.Locale ?? "en_US";
        var userId = socket.UserId;

        lock (lockObject)
        {
            // Remove from old locale if exists
            UnregisterSocketInternal(socket);

            // Add to new locale queue
            if (!availableSockets.ContainsKey(locale))
                availableSockets[locale] = new Queue<MinecraftSocket>();

            availableSockets[locale].Enqueue(socket);
            socketsByUserId[userId] = socket;

            logger.LogDebug($"Registered socket for user {userId} with locale {locale}");
        }
    }

    public void UnregisterSocket(MinecraftSocket socket)
    {
        lock (lockObject)
        {
            UnregisterSocketInternal(socket);
        }
    }

    private void UnregisterSocketInternal(MinecraftSocket socket)
    {
        var userId = socket.UserId;

        foreach (var kvp in availableSockets.ToList())
        {
            var locale = kvp.Key;
            var queue = kvp.Value;

            if (queue.Any(s => s.UserId == userId))
            {
                availableSockets[locale] = new Queue<MinecraftSocket>(queue.Where(s => s.UserId != userId));
                if (availableSockets[locale].Count == 0)
                    availableSockets.Remove(locale);
            }
        }

        socketsByUserId.Remove(userId);
        logger.LogDebug($"Unregistered socket for user {userId}");
    }

    public async Task<string> RequestProxy(string url, string uploadTo, string locale = null, string regex = null)
    {
        MinecraftSocket targetSocket = null;

        lock (lockObject)
        {
            if (locale != null && availableSockets.ContainsKey(locale) && availableSockets[locale].Count > 0)
            {
                targetSocket = availableSockets[locale].Dequeue();
            }
            else
            {
                // Fallback to any available socket
                var anyQueue = availableSockets.Values.FirstOrDefault(q => q.Count > 0);
                if (anyQueue != null)
                    targetSocket = anyQueue.Dequeue();
            }
        }

        if (targetSocket == null)
        {
            throw new InvalidOperationException("No proxy users available" + (locale != null ? $" for locale {locale}" : ""));
        }

        var requestId = Guid.NewGuid().ToString();
        var request = new ProxyRequest
        {
            id = requestId,
            url = url,
            uploadTo = uploadTo,
            regex = regex
        };

        targetSocket.Send(Response.Create("proxy", new[] { request }));

        // Re-register socket for future requests
        RegisterSocket(targetSocket);

        logger.LogInformation($"Sent proxy request {requestId} to user {targetSocket.UserId} for URL {url}");

        return requestId;
    }

    public async Task<bool> StoreProxyResponse(string id, string requestUrl, string responseBody, int statusCode, string headers, string userId, string locale)
    {
        try
        {
            var response = new ProxyResponseTable
            {
                Id = id,
                RequestUrl = requestUrl,
                ResponseBody = responseBody,
                StatusCode = statusCode,
                Headers = headers,
                UserId = userId,
                Locale = locale,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await GetTable().Insert(response).ExecuteAsync();
            // Increment user's proxy counter by 1 (async)
            try
            {
                await IncrementCounterAsync(userId, 1);
            }
            catch (Exception exCnt)
            {
                logger.LogError(exCnt, $"Failed to increment proxy counter for user {userId}");
            }
            logger.LogInformation($"Stored proxy response {id} from user {userId}");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to store proxy response {id}");
            return false;
        }
    }

    /// <summary>
    /// Increment or decrement the proxy counter for a user. Use negative amounts to decrement.
    /// </summary>
    public async Task IncrementCounterAsync(string userId, long amount = 1)
    {
        if (string.IsNullOrEmpty(userId))
            return;

        // Using LINQ update to increment counter column
        await GetCounterTable()
            .Where(x => x.UserId == userId)
            .Select(x => new ProxyCounterTable { RequestCount = amount })
            .Update()
            .ExecuteAsync();
    }

    /// <summary>
    /// Get the current proxy points for a user. Returns 0 on error or if not present.
    /// </summary>
    public async Task<long> GetPointsAsync(string userId)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
                return 0;

            return await GetCounterTable()
                .Where(x => x.UserId == userId)
                .Select(x => x.RequestCount)
                .FirstOrDefault()
                .ExecuteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to read proxy points for user {userId}");
            return 0;
        }
    }

    /// <summary>
    /// Adjust (decrease/increase) points for a user. Use negative delta to subtract.
    /// </summary>
    public async Task AdjustPointsAsync(string userId, long delta)
    {
        await IncrementCounterAsync(userId, delta);
    }

    public async Task<ProxyResponseTable> GetProxyResponse(string id)
    {
        try
        {
            var response = await GetTable()
                .Where(x => x.Id == id)
                .FirstOrDefault()
                .ExecuteAsync();

            if (response != null && response.CreatedAt.AddHours(1) < DateTimeOffset.UtcNow)
            {
                // Expired (though TTL should handle this)
                logger.LogDebug($"Proxy response {id} has expired");
                return null;
            }

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to get proxy response {id}");
            return null;
        }
    }

    public int GetAvailableSocketCount(string locale = null)
    {
        lock (lockObject)
        {
            if (locale != null)
            {
                return availableSockets.ContainsKey(locale) ? availableSockets[locale].Count : 0;
            }
            return availableSockets.Values.Sum(q => q.Count);
        }
    }

    public Dictionary<string, int> GetSocketCountByLocale()
    {
        lock (lockObject)
        {
            return availableSockets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
        }
    }
}
