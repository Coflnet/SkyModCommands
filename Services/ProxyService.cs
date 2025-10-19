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
            var createTableQuery = @"
                CREATE TABLE IF NOT EXISTS proxy_responses (
                    id text PRIMARY KEY,
                    request_url text,
                    response_body text,
                    status_code int,
                    headers text,
                    user_id text,
                    locale text,
                    created_at timestamp
                ) WITH default_time_to_live = 3600;";

            session.Execute(createTableQuery);
            logger.LogInformation("Proxy responses table initialized");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize proxy_responses table");
        }
    }

    public Table<ProxyResponseTable> GetTable()
    {
        return new Table<ProxyResponseTable>(session);
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
            logger.LogInformation($"Stored proxy response {id} from user {userId}");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to store proxy response {id}");
            return false;
        }
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
