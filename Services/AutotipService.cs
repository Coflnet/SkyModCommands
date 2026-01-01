using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Coflnet.Sky.Proxy.Client.Api;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Tutorials;
using Coflnet.Sky.PlayerName.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Service for managing autotip functionality
/// </summary>
public class AutotipService
{
    private readonly ISession session;
    private readonly IConfiguration config;
    private readonly ILogger<AutotipService> logger;
    private readonly IProxyApi proxyApi;
    private readonly IPlayerNameApi playerNameApi;

    // Supported gamemodes as defined in the requirements
    public static readonly string[] SupportedGamemodes = ["legacy", "blitz", "megawalls", "arcade", "skywars", "smash", "uhc", "cnc", "warlords", "tnt"];

    // Timer for automatic tipping
    private readonly Timer autotipTimer;

    // Timer for updating booster list (every 30 minutes)
    private readonly Timer boosterUpdateTimer;

    // Http client for lightweight external lookups (mojang / name resolution)
    private readonly HttpClient httpClient;

    // All active connections that should run autotip
    private readonly ConcurrentDictionary<string, IMinecraftSocket> activeConnections = new();
    // Last player that was tipped by a given userId (used to act on tip failures)
    private readonly ConcurrentDictionary<string, string> lastTippedByUserId = new();

    // Global tip blacklist (player name -> marker). If a player is blacklisted here,
    // autotip should avoid tipping them globally.
    private readonly ConcurrentDictionary<string, byte> tipBlacklist = new();

    // Cache of active boosters by gamemode
    private readonly ConcurrentDictionary<string, List<ActiveBooster>> activeBoosters = new();

    // Lock for booster cache updates
    private readonly object boosterLock = new();

    public AutotipService(ISession session, IConfiguration config, ILogger<AutotipService> logger, IProxyApi proxyApi, IPlayerNameApi playerNameApi)
    {
        this.session = session;
        this.config = config;
        this.logger = logger;
        this.proxyApi = proxyApi;
        this.playerNameApi = playerNameApi;

        InitializeTables();

        // Start the 1-minute autotip timer
        autotipTimer = new Timer(ExecuteAutotipCycle, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));

        // Start the booster update timer - runs immediately, then every 30 minutes
        boosterUpdateTimer = new Timer(UpdateBoosterCache, null, dueTime: 0, period: (int)TimeSpan.FromMinutes(30).TotalMilliseconds);

        httpClient = new HttpClient();

        logger.LogInformation("AutotipService initialized with 1-minute timer and 30-minute booster update");
    }

    /// <summary>
    /// Initialize Cassandra tables for autotip functionality
    /// </summary>
    private void InitializeTables()
    {
        try
        {
            var entryTable = GetAutotipTable();
            entryTable.CreateIfNotExists();

            // Set 7-day TTL on both tables to keep recent data manageable
            var keyspace = session.Keyspace;
            if (!string.IsNullOrEmpty(keyspace))
            {
                try
                {
                    // Check and set TTL for main table
                    var rs = session.Execute($"SELECT default_time_to_live FROM system_schema.tables WHERE keyspace_name = '{keyspace}' AND table_name = 'autotip_entries';");
                    var row = rs.FirstOrDefault();
                    if (row != null)
                    {
                        int? currentTtl = null;
                        if (!row.IsNull("default_time_to_live"))
                        {
                            currentTtl = row.GetValue<int>("default_time_to_live");
                        }

                        if (!currentTtl.HasValue || currentTtl.Value != 604800) // 7 days
                        {
                            session.Execute($"ALTER TABLE {keyspace}.autotip_entries WITH default_time_to_live = 604800");
                            logger.LogInformation("Set TTL for autotip_entries table to 7 days");
                        }
                    }

                    // Check and set TTL for recent table
                    rs = session.Execute($"SELECT default_time_to_live FROM system_schema.tables WHERE keyspace_name = '{keyspace}' AND table_name = 'autotip_recent_by_gamemode';");
                    row = rs.FirstOrDefault();
                    if (row != null)
                    {
                        int? currentTtl = null;
                        if (!row.IsNull("default_time_to_live"))
                        {
                            currentTtl = row.GetValue<int>("default_time_to_live");
                        }

                        if (!currentTtl.HasValue || currentTtl.Value != 604800) // 7 days
                        {
                            session.Execute($"ALTER TABLE {keyspace}.autotip_recent_by_gamemode WITH default_time_to_live = 604800");
                            logger.LogInformation("Set TTL for autotip_recent_by_gamemode table to 7 days");
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Could not set TTL on autotip tables");
                }
            }

            logger.LogInformation("Autotip tables initialized successfully");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to initialize autotip tables");
            throw;
        }
    }

    /// <summary>
    /// Get the main autotip entries table
    /// </summary>
    public Table<AutotipEntry> GetAutotipTable()
    {
        return new Table<AutotipEntry>(session, new MappingConfiguration().Define(
            new Map<AutotipEntry>()
                .PartitionKey(u => u.UserId)
                .TableName("autotip_entries")
                .ClusteringKey(u => u.TippedAt, SortOrder.Descending)
                .Column(u => u.TippedPlayerUuid, cm => cm.WithName("tipped_player_uuid"))
                .Column(u => u.TippedPlayerName, cm => cm.WithName("tipped_player_name"))
                .Column(u => u.Amount, cm => cm.WithName("amount"))
                .Column(u => u.IsAutomatic, cm => cm.WithName("is_automatic"))
        ));
    }


    /// <summary>
    /// Register an active connection for autotip processing
    /// </summary>
    public void RegisterConnection(IMinecraftSocket socket)
    {
        if (socket?.UserId != null && !activeConnections.ContainsKey(socket.UserId))
        {
            activeConnections.TryAdd(socket.UserId, socket);
            logger.LogDebug($"Registered connection for autotip: {socket.SessionInfo?.McName ?? "Unknown"}");
        }
    }

    /// <summary>
    /// Unregister a connection from autotip processing
    /// </summary>
    public void UnregisterConnection(string userId)
    {
        if (!string.IsNullOrEmpty(userId) && activeConnections.TryRemove(userId, out var socket))
        {
            logger.LogDebug($"Unregistered connection from autotip: {socket.SessionInfo?.McName ?? "Unknown"}");
        }
    }

    /// <summary>
    /// Update the booster cache from the Hypixel API
    /// </summary>
    private async void UpdateBoosterCache(object state)
    {
        try
        {
            logger.LogInformation("Updating booster cache from Hypixel API");

            // Request boosters via the proxy API so we don't need a Hypixel API key locally
            var res = await proxyApi.ProxyHypixelGetAsync("/v2/boosters");
            if (res == null)
            {
                logger.LogWarning("Proxy API returned null for boosters request");
                return;
            }

            // The proxy client typically returns a JSON-encoded string, unwrap as done elsewhere
            var json = JsonConvert.DeserializeObject<string>(res);
            var boosterResponse = JsonConvert.DeserializeObject<BoosterResponse>(json);
            if (boosterResponse == null || !boosterResponse.success || boosterResponse.boosters == null || boosterResponse.boosters.Count == 0)
            {
                logger.LogInformation("No boosters returned by proxy/hypixel API");
                lock (boosterLock)
                {
                    activeBoosters.Clear();
                }
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var newBoosters = new ConcurrentDictionary<string, List<ActiveBooster>>();
            var uuidsToFetch = new HashSet<string>();

            // Filter boosters to only those with >= 30 minutes remaining and supported gamemodes
            var validBoosters = new List<(BoosterEntry entry, string gamemode)>();

            foreach (var booster in boosterResponse.boosters)
            {
                var expirationTime = booster.dateActivated * 1000 + booster.length * 1000; // Convert to milliseconds
                var timeRemaining = expirationTime - now;

                // Only keep boosters that are active for longer than the update interval (30 minutes)
                // but expiring within 1 hour (prefer boosters that will expire soon)
                if (timeRemaining >= 30 * 60 * 1000 && timeRemaining <= 60 * 60 * 1000 && GameTypeMapper.IsSupportedForAutotip(booster.gameType))
                {
                    var gamemode = GameTypeMapper.GetGamemode(booster.gameType);
                    if (gamemode != null)
                    {
                        validBoosters.Add((booster, gamemode));
                        // main purchaser
                        if (!string.IsNullOrEmpty(booster.purchaserUuid))
                            uuidsToFetch.Add(booster.purchaserUuid);

                        // stacked may be an array of uuids or boolean true; only extract when array
                        if (booster.stacked != null && booster.stacked.Type == JTokenType.Array)
                        {
                            foreach (var t in booster.stacked)
                            {
                                try
                                {
                                    var s = t.ToString();
                                    if (!string.IsNullOrEmpty(s))
                                        uuidsToFetch.Add(s);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }

            if (validBoosters.Count == 0)
            {
                logger.LogInformation("No valid boosters found for autotip");
                lock (boosterLock)
                {
                    activeBoosters.Clear();
                }
                return;
            }

            // Fetch all usernames for the booster UUIDs
            var uuidToNameMap = await FetchPlayerNamesAsync(uuidsToFetch);

            // Build the new booster cache
            foreach (var item in validBoosters)
            {
                var entry = item.entry;
                var gamemode = item.gamemode;

                if (!uuidToNameMap.TryGetValue(entry.purchaserUuid, out var playerName))
                {
                    logger.LogWarning($"Failed to fetch name for UUID {entry.purchaserUuid}");
                    // continue building other boosters but skip purchaser entry
                    playerName = null;
                }

                var activeBooster = new ActiveBooster
                {
                    purchaserUuid = entry.purchaserUuid,
                    purchaserName = playerName,
                    gamemode = gamemode,
                    timeActivated = entry.dateActivated,
                    timeExpires = entry.dateActivated * 1000 + entry.length * 1000
                };

                if (!newBoosters.ContainsKey(gamemode))
                {
                    newBoosters[gamemode] = new List<ActiveBooster>();
                }

                newBoosters[gamemode].Add(activeBooster);

                // Also add stacked boosters (if any) as separate entries when their names were resolved
                if (entry.stacked != null && entry.stacked.Type == JTokenType.Array)
                {
                    foreach (var t in entry.stacked)
                    {
                        var sid = t.ToString();
                        if (string.IsNullOrEmpty(sid))
                            continue;
                        if (!uuidToNameMap.TryGetValue(sid, out var stackedName))
                            continue;

                        var stackedBooster = new ActiveBooster
                        {
                            purchaserUuid = sid,
                            purchaserName = stackedName,
                            gamemode = gamemode,
                            timeActivated = entry.dateActivated,
                            timeExpires = entry.dateActivated * 1000 + entry.length * 1000
                        };

                        newBoosters[gamemode].Add(stackedBooster);
                    }
                }
            }

            // Update the cache atomically
            lock (boosterLock)
            {
                activeBoosters.Clear();
                foreach (var kvp in newBoosters)
                {
                    activeBoosters[kvp.Key] = kvp.Value;
                }
            }

            var totalBoosters = validBoosters.Count;
            var gamemodes = string.Join(", ", newBoosters.Keys);
            logger.LogInformation($"Updated booster cache: {totalBoosters} active boosters across gamemodes: {gamemodes}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating booster cache");
        }
    }

    /// <summary>
    /// Fetch player names for a list of UUIDs
    /// </summary>
    private async Task<Dictionary<string, string>> FetchPlayerNamesAsync(HashSet<string> uuids)
    {
        var result = new Dictionary<string, string>();

        if (uuids.Count == 0)
            return result;

        try
        {
            var tasks = new List<Task>();
            var lockObj = new object();

            foreach (var uuid in uuids)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var name = await playerNameApi.PlayerNameNameUuidGetAsync(uuid);
                        if (!string.IsNullOrEmpty(name))
                        {
                            // PlayerNameApi returns name in quotes, trim them
                            var cleanName = name.Trim('"');
                            lock (lockObj)
                            {
                                result[uuid] = cleanName;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, $"Failed to fetch name for UUID {uuid}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching player names");
        }

        return result;
    }

    /// <summary>
    /// Get a list of active boosters for a specific gamemode
    /// </summary>
    private List<ActiveBooster> GetBoostersForGamemode(string gamemode)
    {
        lock (boosterLock)
        {
            return activeBoosters.TryGetValue(gamemode, out var boosters)
                ? new List<ActiveBooster>(boosters)
                : new List<ActiveBooster>();
        }
    }

    /// <summary>
    /// Check if a player is currently a network booster in any gamemode
    /// </summary>
    private bool IsNetworkBooster(string playerName)
    {
        if (string.IsNullOrEmpty(playerName))
            return false;

        lock (boosterLock)
        {
            return activeBoosters.Values.Any(boosters => 
                boosters.Any(b => b.purchaserName?.Equals(playerName, StringComparison.OrdinalIgnoreCase) ?? false));
        }
    }

    /// <summary>
    /// Get all currently active booster names across all gamemodes
    /// </summary>
    private HashSet<string> GetAllActiveBoosterNames()
    {
        lock (boosterLock)
        {
            return activeBoosters.Values
                .SelectMany(boosters => boosters)
                .Where(b => !string.IsNullOrEmpty(b.purchaserName))
                .Select(b => b.purchaserName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Count how many network boosters each player has active across all gamemodes
    /// </summary>
    private Dictionary<string, int> GetBoosterCountsByPlayer()
    {
        lock (boosterLock)
        {
            return activeBoosters.Values
                .SelectMany(boosters => boosters)
                .Where(b => !string.IsNullOrEmpty(b.purchaserName))
                .GroupBy(b => b.purchaserName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }
    }


    /// <summary>
    /// Record a completed tip in the database
    /// </summary>
    private void RecordTip(string userId, string targetPlayer, string gamemode, long amount, bool isAutomatic)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;

            var entry = new AutotipEntry
            {
                UserId = userId,
                Gamemode = gamemode.ToLowerInvariant(),
                TippedPlayerUuid = "unknown", // UUID not needed for filtering
                TippedAt = now,
                TippedPlayerName = targetPlayer,
                Amount = amount,
                IsAutomatic = isAutomatic
            };

            // Store in single table - fire and forget
            _ = GetAutotipTable().Insert(entry).ExecuteAsync();

            logger.LogDebug($"Recorded tip: {userId} -> {targetPlayer} ({gamemode}, {amount} coins, auto: {isAutomatic})");
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Failed to record tip for user {userId}");
        }
    }

    /// <summary>
    /// Get online players to tip from active connections, preferring active boosters
    /// </summary>
    private string GetOnlinePlayersInGamemode(IMinecraftSocket socket, string gamemode, HashSet<string> tippedTodayNames, HashSet<string> tippedBoostersLastHour)
    {
        // First, check if there are any active boosters for this gamemode
        var boosters = GetBoostersForGamemode(gamemode);

        if (boosters.Count > 0)
        {
            // Select a random booster to prefer
            // Respect the global blacklist when selecting boosters
            if (boosters.Any(b => b.purchaserName == "Ekwav" && !tippedTodayNames.Contains("Ekwav")))
            {
                logger.LogInformation($"Preferring booster Ekwav for {gamemode}");
                return "Ekwav";
            }
            // Filter boosters by the global blacklist, daily tip tracking, and hour-since-last-tip rule
            // Since Hypixel tips boosters in all modes when running one tip, we check if this user tipped this booster in the last hour
            var candidates = boosters
                .Where(b => !string.IsNullOrEmpty(b.purchaserName)
                    && !tipBlacklist.ContainsKey(b.purchaserName)
                    && !tippedTodayNames.Contains(b.purchaserName)
                    && !tippedBoostersLastHour.Contains(b.purchaserName))
                .ToList();
            
            if (candidates.Count > 0)
            {
                // Prioritize boosters with multiple network boosters active
                // Since Hypixel tips boosters in all modes, tipping a player with more boosters provides more value
                var boosterCounts = GetBoosterCountsByPlayer();
                var sortedCandidates = candidates
                    .OrderByDescending(b => boosterCounts.TryGetValue(b.purchaserName, out var count) ? count : 0)
                    .ToList();
                
                var selectedBooster = sortedCandidates.First();
                var boosterCount = boosterCounts.TryGetValue(selectedBooster.purchaserName, out var bc) ? bc : 1;
                logger.LogInformation($"Preferring booster {selectedBooster.purchaserName} for {gamemode} (has {boosterCount} active network boosters)");
                return selectedBooster.purchaserName;
            }

        }

        // Fallback to online players from active connections
        var playerNames = new List<string>();

        // Get all connected players from active sessions
        foreach (var connection in activeConnections.Values)
        {
            var playerName = connection.SessionInfo?.McName;
            if (connection?.sessionLifesycle?.AccountSettings?.Value.BlockAutotip ?? true)
                continue;
            if (connection == socket)
                continue;
            if (!string.IsNullOrEmpty(playerName))
            {
                // Respect global tip blacklist
                if (tipBlacklist.ContainsKey(playerName))
                    continue;

                // Check if already tipped today in this gamemode
                if (tippedTodayNames.Contains(playerName))
                    continue;

                playerNames.Add(playerName);
            }
        }

        // Prefer Ekwav if present and not already tipped today
        if (playerNames.Contains("Ekwav"))
        {
            return "Ekwav";
        }

        return playerNames.OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
    }

    /// <summary>
    /// Get list of player names that were already tipped by this user in the last 24 hours for the given gamemode
    /// </summary>
    private async Task<HashSet<string>> GetTippedPlayersLast24Hours(string userId, string gamemode)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            
            // Query with userId and gamemode - Cassandra will efficiently return all players tipped in this game
            var tippedPlayers = await GetAutotipTable()
                .Where(t => t.UserId == userId && t.Gamemode == gamemode.ToLowerInvariant() && t.TippedAt > cutoff)
                .ExecuteAsync();

            return tippedPlayers.Select(t => t.TippedPlayerName).ToHashSet();
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error getting tipped players in last 24h for user {userId} in {gamemode}");
            return new HashSet<string>();
        }
    }

    /// <summary>
    /// Send tip to Hypixel (mock implementation)
    /// </summary>
    private async Task<bool> SendTipToHypixel(IMinecraftSocket socket, string targetPlayer, string gamemode)
    {
        try
        {
            // Send tip command to Minecraft
            socket.ExecuteCommand($"/tip {targetPlayer} {gamemode}");

            logger.LogInformation($"Sent tip command: /tip {targetPlayer} {gamemode} from {socket.SessionInfo?.McName}");
            try
            {
                if (!string.IsNullOrEmpty(socket.UserId))
                {
                    lastTippedByUserId[socket.UserId] = targetPlayer;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to store last tipped player");
            }
            await socket.TriggerTutorial<AutotipTutorial>();
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Failed to send tip to {targetPlayer}");
            return false;
        }
    }

    /// <summary>
    /// Execute automatic tipping cycle for all registered connections
    /// </summary>
    private async void ExecuteAutotipCycle(object state)
    {
        if (activeConnections.IsEmpty)
            return;

        logger.LogDebug($"Starting autotip cycle for {activeConnections.Count} connections");

        var tasks = new List<Task>();

        foreach (var connection in activeConnections.Values.ToList())
        {
            await Task.Delay(100); // Stagger to avoid spikes
            tasks.Add(ProcessConnectionAutotip(connection));
        }

        try
        {
            await Task.WhenAll(tasks);
            logger.LogDebug("Autotip cycle completed");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error during autotip cycle");
        }
    }

    /// <summary>
    /// Process autotip for a single connection
    /// </summary>
    private async Task ProcessConnectionAutotip(IMinecraftSocket socket)
    {
        try
        {
            // Check if user has autotip blocked
            var blocksTip = socket.sessionLifesycle?.AccountSettings?.Value.BlockAutotip;
            if (blocksTip ?? true)
            {
                logger.LogDebug($"Autotip blocked for user {socket.SessionInfo?.McName}");
                return;
            }

            // Load all tips from last 24 hours once
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var recentTips = await GetAutotipTable()
                .Where(t => t.UserId == socket.UserId && t.TippedAt > cutoff)
                .ExecuteAsync();
            
            var recentTipsList = recentTips.ToList();

            var gamemodeNeedingTip = FindGamemodeNeedingTip(socket.UserId, recentTipsList);

            if (gamemodeNeedingTip == null)
            {
                logger.LogDebug($"No gamemode needs tip for user {socket.SessionInfo?.McName}");
                return;
            }

            // Get tipped players for this specific gamemode from the already loaded data
            var tippedInGamemode = recentTipsList
                .Where(t => t.Gamemode == gamemodeNeedingTip.ToLowerInvariant())
                .Select(t => t.TippedPlayerName)
                .ToHashSet();

            // Get all current active boosters
            var activeBoosterNames = GetAllActiveBoosterNames();

            // Get boosters this user has tipped in the last hour (across all gamemodes)
            // Since Hypixel tips boosters in all modes, we need to avoid tipping them again within an hour
            var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
            var tippedBoostersLastHour = recentTipsList
                .Where(t => t.TippedAt > oneHourAgo && activeBoosterNames.Contains(t.TippedPlayerName))
                .Select(t => t.TippedPlayerName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Get online players in that gamemode
            var targetPlayer = GetOnlinePlayersInGamemode(socket, gamemodeNeedingTip, tippedInGamemode, tippedBoostersLastHour);

            if (targetPlayer == null)
            {
                logger.LogDebug($"No online players found in {gamemodeNeedingTip}");
                return;
            }

            // Execute automatic tip
            var success = await SendTipToHypixel(socket, targetPlayer, gamemodeNeedingTip);

            if (success)
            {
                RecordTip(socket.UserId, targetPlayer, gamemodeNeedingTip, 100, true);
                logger.LogInformation($"Automatic tip sent: {socket.SessionInfo?.McName} -> {targetPlayer} in {gamemodeNeedingTip}");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error processing autotip for user {socket.SessionInfo?.McName}");
        }
    }

    /// <summary>
    /// Find a gamemode where the user hasn't tipped anyone in the last 24 hours
    /// </summary>
    private string FindGamemodeNeedingTip(string userId, List<AutotipEntry> recentTips)
    {
        try
        {
            // Determine the most recent tip timestamp per gamemode from the provided recentTips
            // If a gamemode has no tips in the last 24h, treat its last tip as DateTimeOffset.MinValue
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddMinutes(-30); // need at least 30 minutes since last tip

            var lastTipByGamemode = new Dictionary<string, DateTimeOffset>();

            // Initialize with MinValue so untipped gamemodes are considered oldest
            foreach (var gm in SupportedGamemodes)
            {
                lastTipByGamemode[gm.ToLowerInvariant()] = DateTimeOffset.MinValue;
            }

            foreach (var tip in recentTips)
            {
                if (string.IsNullOrEmpty(tip.Gamemode))
                    continue;

                var gm = tip.Gamemode.ToLowerInvariant();
                if (!lastTipByGamemode.ContainsKey(gm))
                    continue;

                // keep the most recent tip time for that gamemode
                if (tip.TippedAt > lastTipByGamemode[gm])
                    lastTipByGamemode[gm] = tip.TippedAt;
            }

            // Select the gamemode whose last tip is the oldest but older than 30 minutes
            var candidates = lastTipByGamemode
                .Where(kvp => kvp.Value <= cutoff)
                .OrderBy(kvp => kvp.Value) // oldest first (MinValue will be first)
                .ToList();

            if (candidates.Count == 0)
            {
                // No gamemode has a last tip older than 30 minutes
                return null;
            }

            // Return the gamemode (original casing from SupportedGamemodes) matching the chosen key
            var chosenKey = candidates.First().Key;
            return SupportedGamemodes.FirstOrDefault(g => g.ToLowerInvariant() == chosenKey);
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error finding gamemode needing tip for user {userId}");
            return null;
        }
    }

    /// <summary>
    /// Get tip statistics for a user
    /// </summary>
    public async Task<List<AutotipEntry>> GetUserTipHistory(string userId, int limit = 50)
    {
        try
        {
            var history = await GetAutotipTable()
                .Where(t => t.UserId == userId)
                .Take(limit)
                .ExecuteAsync();

            return history.ToList();
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error getting tip history for user {userId}");
            return new List<AutotipEntry>();
        }
    }

    /// <summary>
    /// Notify the service that a tip failed to be applied. If a specific targetName is given
    /// it indicates the server reported the name cannot be found and we'll schedule a name
    /// resolution attempt. If targetName is null it usually means the server reported the
    /// player is offline — add them to the per-user tip blacklist so autotip avoids them.
    /// </summary>
    public async Task NotifyTipFailedAsync(IMinecraftSocket socket, string targetName, string message)
    {
        try
        {
            if (socket == null || string.IsNullOrEmpty(socket.UserId))
                return;

            var userId = socket.UserId;

            if (!string.IsNullOrEmpty(targetName))
            {
                // The server couldn't find a player by that name — schedule a name resolution
                logger.LogInformation($"Autotip: name not found for '{targetName}', scheduling resolution for user {userId}");

                var nameInfo = await socket.GetPlayerUuid(targetName, false);
                await IndexerClient.TriggerNameUpdate(nameInfo);
                return;
            }

            // No explicit name provided: interpret as 'That player is not online' -> blacklist the last tipped globally
            if (lastTippedByUserId.TryGetValue(userId, out var last))
            {
                if (!string.IsNullOrEmpty(last))
                {
                    tipBlacklist.TryAdd(last, 0);
                    logger.LogInformation($"Autotip: added offline player '{last}' to global tip blacklist based on {message}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "NotifyTipFailedAsync failed");
        }
    }

    /// <summary>
    /// Clean up resources
    /// </summary>
    public void Dispose()
    {
        autotipTimer?.Dispose();
        boosterUpdateTimer?.Dispose();
        activeConnections.Clear();
        activeBoosters.Clear();
        logger.LogInformation("AutotipService disposed");
    }
}