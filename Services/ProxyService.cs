using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Models;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.ModCommands.Services;

public class ProxyService
{
    private readonly ISession session;
    private readonly IConfiguration config;
    private readonly ILogger<ProxyService> logger;
    private readonly HttpClient httpClient;
    private readonly Dictionary<string, Queue<MinecraftSocket>> availableSockets = new();
    private readonly Dictionary<string, MinecraftSocket> socketsByUserId = new();
    private readonly object lockObject = new();

    public ProxyService(ISession session, IConfiguration config, ILogger<ProxyService> logger, HttpClient httpClient)
    {
        this.session = session;
        this.config = config;
        this.logger = logger;
        this.httpClient = httpClient;

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

            var infoTable = GetInfoTable();
            infoTable.CreateIfNotExists();

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

            logger.LogInformation("Proxy responses, counters, and info tables initialized");
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

    public Table<ProxyUserInfo> GetInfoTable()
    {
        return new Table<ProxyUserInfo>(session);
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

        // Persist a small placeholder record so we can later map responses back to the user
        try
        {
            var placeholder = new ProxyResponseTable
            {
                Id = requestId,
                RequestUrl = url,
                UserId = targetSocket.UserId,
                Locale = locale,
                CreatedAt = DateTimeOffset.UtcNow
            };
            // Insert will create the row; later the full response will overwrite these columns.
            await GetTable().Insert(placeholder).ExecuteAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, $"Failed to persist placeholder for proxy request {requestId}");
        }

        return requestId;
    }

    public async Task<bool> StoreProxyResponse(string id, string requestUrl, string responseBody, int statusCode, string headers, string userId, string locale)
    {
        try
        {
            // If userId wasn't provided, try to resolve it from the placeholder row we created when dispatching the request.
            if (string.IsNullOrEmpty(userId))
            {
                try
                {
                    var existing = await GetTable().Where(x => x.Id == id).FirstOrDefault().ExecuteAsync();
                    if (existing != null && !string.IsNullOrEmpty(existing.UserId))
                    {
                        userId = existing.UserId;
                    }
                    if (existing != null && !string.IsNullOrEmpty(existing.Locale))
                    {
                        locale = existing.Locale;
                    }
                    if (string.IsNullOrEmpty(requestUrl) && existing != null && !string.IsNullOrEmpty(existing.RequestUrl))
                    {
                        requestUrl = existing.RequestUrl;
                    }
                }
                catch (Exception exResolve)
                {
                    logger.LogWarning(exResolve, $"Failed to resolve userId for proxy response {id}");
                }
            }
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

    /// <summary>
    /// Fetches IP geolocation and quality info through the user's connection
    /// </summary>
    public async Task<ProxyUserInfo> FetchIpInfoAsync(MinecraftSocket socket)
    {
        var userId = socket.UserId;

        try
        {
            // Fetch IP info from ipinfo.io through user's proxy
            var request = new ProxyRequest
            {
                id = Guid.NewGuid().ToString(),
                url = "https://ipinfo.io/what-is-my-ip",
            };

            socket.Send(Response.Create("proxy", new[] { request }));

            // Wait 20 seconds for response
            await Task.Delay(20000);

            var ipInfoResponse = await GetProxyResponse(request.id);
            if (ipInfoResponse == null || string.IsNullOrEmpty(ipInfoResponse.ResponseBody))
            {
                logger.LogWarning($"Failed to get ipinfo.io response for user {userId}");
                return null;
            }

            // Parse HTML to extract IP and geolocation data
            var html = ipInfoResponse.ResponseBody;
            
            // Extract IP from h1: <h1 class="h1-data-pages">87.159.24.199</h1>
            string ipAddress = ExtractFromHtml(html, @"<h1\s+class=""h1-data-pages"">([0-9\.]+)</h1>");
            
            // Extract from table rows in the geolocation section
            string city = ExtractTableValue(html, "City");
            string latLonStr = ExtractTableValue(html, "Coordinates");
            
            // Extract country code from link: <a href="/countries/de">Germany</a>
            string countryCode = ExtractCountryCode(html);
            
            // Extract ASN type (ISP, hosting, or business)
            string asnType = ExtractAsnType(html);

            if (string.IsNullOrEmpty(ipAddress))
            {
                logger.LogWarning($"No IP address found in ipinfo.io response for user {userId}");
                return null;
            }

            // Parse latitude and longitude from coordinates string
            double? latitude = null;
            double? longitude = null;
            if (!string.IsNullOrEmpty(latLonStr))
            {
                var parts = latLonStr.Split(',');
                if (parts.Length == 2)
                {
                    if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat))
                        latitude = lat;
                    if (double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                        longitude = lon;
                }
            }

            // Build ProxyUserInfo with essential fields only
            var info = new ProxyUserInfo
            {
                UserId = userId,
                IpAddress = ipAddress,
                CountryCode = countryCode,
                Latitude = latitude,
                Longitude = longitude,
                City = city,
                AsnType = asnType?.ToLowerInvariant(), // Normalize to lowercase: "isp", "hosting", "business"
                LastUpdated = DateTimeOffset.UtcNow
            };

            // Store in database
            await GetInfoTable().Insert(info).ExecuteAsync();

            logger.LogInformation($"Stored IP info for user {userId}: {ipAddress} ({info.CountryCode}), ASN Type={info.AsnType}");

            return info;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to fetch IP info for user {userId}");
            return null;
        }
    }

    /// <summary>
    /// Gets stored IP info for a user
    /// </summary>
    public async Task<ProxyUserInfo> GetUserIpInfoAsync(string userId)
    {
        try
        {
            return await GetInfoTable()
                .Where(x => x.UserId == userId)
                .FirstOrDefault()
                .ExecuteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to get IP info for user {userId}");
            return null;
        }
    }

    /// <summary>
    /// Gets the best country code for routing based on IP quality (prefers ISP type)
    /// </summary>
    public async Task<string> GetBestCountryForUserAsync(string userId)
    {
        var info = await GetUserIpInfoAsync(userId);
        if (info == null)
            return null;

        // Prefer ISP type over hosting/business
        if (info.AsnType == "isp")
            return info.CountryCode;

        // If not ISP, still return country code but it may be deprioritized elsewhere
        return info.CountryCode;
    }

    /// <summary>
    /// Selects the best available socket for proxying, preferring ISP type over hosting/business
    /// </summary>
    public async Task<MinecraftSocket> SelectBestSocketAsync(string locale = null)
    {
        List<MinecraftSocket> candidates = new();

        lock (lockObject)
        {
            if (locale != null && availableSockets.ContainsKey(locale) && availableSockets[locale].Count > 0)
            {
                candidates = availableSockets[locale].ToList();
            }
            else
            {
                // Fallback to any available socket
                candidates = availableSockets.Values.SelectMany(q => q).ToList();
            }
        }

        if (candidates.Count == 0)
            return null;

        // Fetch IP info for all candidates
        var socketInfoPairs = new List<(MinecraftSocket socket, ProxyUserInfo info)>();
        foreach (var socket in candidates)
        {
            var info = await GetUserIpInfoAsync(socket.UserId);
            socketInfoPairs.Add((socket, info));
        }

        // Prioritize: ISP type (0) > hosting/business (1) > no info (2)
        var best = socketInfoPairs
            .OrderBy(pair => pair.info == null ? 2 : (pair.info.AsnType == "isp" ? 0 : 1))
            .ThenBy(pair => Random.Shared.Next())
            .FirstOrDefault();

        return best.socket;
    }

    /// <summary>
    /// Extracts text from HTML using a regex pattern
    /// </summary>
    private string ExtractFromHtml(string html, string pattern)
    {
        try
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, $"Failed to extract data from HTML using pattern: {pattern}");
        }
        return null;
    }

    /// <summary>
    /// Extracts value from HTML table row with specific label
    /// </summary>
    private string ExtractTableValue(string html, string label)
    {
        var pattern = $@"<td>{label}</td>\s*<td>([^<]+)</td>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (match.Success && match.Groups.Count > 1)
        {
            return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
        }
        return null;
    }

    /// <summary>
    /// Extracts country code from ipinfo.io HTML
    /// </summary>
    private string ExtractCountryCode(string html)
    {
        // Extract from table row with country link: <td>Country</td><td>...<a href="/countries/de">Germany</a></td>
        var match = Regex.Match(
            html,
            @"<td>Country</td>\s*<td>.*?/countries/([a-z]{2})""",
            RegexOptions.IgnoreCase
        );
        return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Extracts ASN type from ipinfo.io HTML. Handles both table row:
    ///   <td>ASN type</td><td>ISP</td>
    /// and heading+span pattern:
    ///   <h6 class="uppercase-small-title">ASN type</h6>
    ///   <span>ISP</span>
    /// Whitespace/newlines are ignored.
    /// </summary>
    private string ExtractAsnType(string html)
    {
        try
        {
            // Try table row first
            var tablePattern = @"<td>\s*ASN\s*type\s*</td>\s*<td>\s*([^<]+?)\s*</td>";
            var m = Regex.Match(html, tablePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success && m.Groups.Count > 1)
            {
                return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim()).ToLowerInvariant();
            }

            // Try heading + span pattern
            var hsPattern = @"<h6[^>]*>\s*ASN\s*type\s*</h6>\s*<span[^>]*>\s*([^<]+?)\s*</span>";
            m = Regex.Match(html, hsPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success && m.Groups.Count > 1)
            {
                return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim()).ToLowerInvariant();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to extract ASN type from HTML");
        }
        return null;
    }
}
