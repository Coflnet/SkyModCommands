using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Tutorials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Service for managing autotip functionality
/// </summary>
public class AutotipService
{
    private readonly ISession session;
    private readonly IConfiguration config;
    private readonly ILogger<AutotipService> logger;

    // Supported gamemodes as defined in the requirements
    public static readonly string[] SupportedGamemodes = { "arcade", "skywars", "tntgames", "legacy" };
    
    // Cache to track recent tips to avoid spamming
    private readonly ConcurrentDictionary<string, DateTime> recentTips = new();
    
    // Timer for automatic tipping
    private readonly Timer autotipTimer;
    
    // All active connections that should run autotip
    private readonly ConcurrentDictionary<string, IMinecraftSocket> activeConnections = new();

    public AutotipService(ISession session, IConfiguration config, ILogger<AutotipService> logger)
    {
        this.session = session;
        this.config = config;
        this.logger = logger;
        
        InitializeTables();
        
        // Start the 1-minute autotip timer
        autotipTimer = new Timer(ExecuteAutotipCycle, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        
        logger.LogInformation("AutotipService initialized with 1-minute timer");
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
    /// Check if user has tipped someone in the specified gamemode recently
    /// </summary>
    private async Task<bool> HasRecentTipInGamemode(string userId, string gamemode)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-1); // Consider tips from last hour as "recent"
            
            var recent = await GetAutotipTable()
                .Where(t => t.UserId == userId && t.Gamemode == gamemode && t.TippedAt > cutoff)
                .Take(1)
                .ExecuteAsync();

            return recent.Any();
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error checking recent tips for user {userId} in gamemode {gamemode}");
            return true; // Err on the side of caution - don't tip if we can't check
        }
    }

    /// <summary>
    /// Record a completed tip in the database
    /// </summary>
    private async Task RecordTip(string userId, string targetPlayer, string gamemode, long amount, bool isAutomatic)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var targetUuid = await GetPlayerUuid(targetPlayer); // This would need to be implemented

            var entry = new AutotipEntry
            {
                UserId = userId,
                Gamemode = gamemode.ToLowerInvariant(),
                TippedAt = now,
                TippedPlayerUuid = targetUuid ?? "unknown",
                TippedPlayerName = targetPlayer,
                Amount = amount,
                IsAutomatic = isAutomatic
            };

            var recentEntry = new AutotipRecentEntry
            {
                Gamemode = gamemode.ToLowerInvariant(),
                UserId = userId,
                TippedAt = now,
                TippedPlayerUuid = targetUuid ?? "unknown",
                TippedPlayerName = targetPlayer,
                Amount = amount,
                IsAutomatic = isAutomatic
            };

            // Store in both tables
            await GetAutotipTable().Insert(entry).ExecuteAsync();

            logger.LogDebug($"Recorded tip: {userId} -> {targetPlayer} ({gamemode}, {amount} coins, auto: {isAutomatic})");
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Failed to record tip for user {userId}");
        }
    }

    /// <summary>
    /// Get online players to tip from active connections
    /// </summary>
    private async Task<List<string>> GetOnlinePlayersInGamemode(string gamemode)
    {
        await Task.Delay(10); // Small delay to simulate processing
        
        var playerNames = new List<string>();
        
        // Get all connected players from active sessions
        foreach (var connection in activeConnections.Values)
        {
            var playerName = connection.SessionInfo?.McName;
            if (!string.IsNullOrEmpty(playerName))
            {
                playerNames.Add(playerName);
            }
        }
        
        // Prefer Ekwav if present and move to front of list
        if (playerNames.Contains("Ekwav"))
        {
            playerNames.Remove("Ekwav");
            playerNames.Insert(0, "Ekwav");
        }
        
        return playerNames;
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
    /// Get player UUID from name (mock implementation)
    /// </summary>
    private async Task<string> GetPlayerUuid(string playerName)
    {
        // Mock implementation - in reality would use Mojang API
        await Task.Delay(10);
        return Guid.NewGuid().ToString("N");
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
            var accountSettings = socket.sessionLifesycle?.AccountSettings?.Value;
            if (accountSettings != null)
            {
                // Use reflection to check for BlockAutotip property (in case it doesn't exist in the shared library yet)
                var blockAutotipProp = accountSettings.GetType().GetProperty("BlockAutotip");
                if (blockAutotipProp != null && (bool)(blockAutotipProp.GetValue(accountSettings) ?? false))
                {
                    logger.LogDebug($"Autotip blocked for user {socket.SessionInfo?.McName}");
                    return;
                }
            }

            // Find gamemodes where user hasn't tipped anyone recently
            var gamemodeNeedingTip = await FindGamemodeNeedingTip(socket.UserId);
            
            if (gamemodeNeedingTip == null)
            {
                logger.LogDebug($"No gamemode needs tip for user {socket.SessionInfo?.McName}");
                return;
            }

            // Get online players in that gamemode
            var onlinePlayers = await GetOnlinePlayersInGamemode(gamemodeNeedingTip);
            
            if (!onlinePlayers.Any())
            {
                logger.LogDebug($"No online players found in {gamemodeNeedingTip}");
                return;
            }

            // Select a random player to tip
            var random = new Random();
            var targetPlayer = onlinePlayers[random.Next(onlinePlayers.Count)];

            // Execute automatic tip
            var success = await SendTipToHypixel(socket, targetPlayer, gamemodeNeedingTip);
            
            if (success)
            {
                await RecordTip(socket.UserId, targetPlayer, gamemodeNeedingTip, 100, true);
                logger.LogInformation($"Automatic tip sent: {socket.SessionInfo?.McName} -> {targetPlayer} in {gamemodeNeedingTip}");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error processing autotip for user {socket.SessionInfo?.McName}");
        }
    }

    /// <summary>
    /// Find a gamemode where the user hasn't tipped anyone recently
    /// </summary>
    private async Task<string> FindGamemodeNeedingTip(string userId)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-1); // Check tips from last hour
            
            // Get recent tips for this user across all gamemodes
            var recentTips = await GetAutotipTable()
                .Where(t => t.UserId == userId && t.TippedAt > cutoff)
                .ExecuteAsync();

            var tippedGamemodes = recentTips.Select(t => t.Gamemode).ToHashSet();

            // Find gamemodes that haven't been tipped in
            var availableGamemodes = SupportedGamemodes.Where(g => !tippedGamemodes.Contains(g)).ToList();
            
            if (!availableGamemodes.Any())
                return null; // All gamemodes have been tipped in recently

            // Return random gamemode from available ones
            var random = new Random();
            return availableGamemodes[random.Next(availableGamemodes.Count)];
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
    /// Clean up resources
    /// </summary>
    public void Dispose()
    {
        autotipTimer?.Dispose();
        activeConnections.Clear();
        logger.LogInformation("AutotipService disposed");
    }
}