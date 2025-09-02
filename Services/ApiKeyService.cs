using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Core;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Service for managing user API keys
/// </summary>
public class ApiKeyService
{
    private readonly ISession session;
    private readonly ILogger<ApiKeyService> logger;
    private readonly Table<ApiKey> apiKeyTable;

    public ApiKeyService(ISession session, ILogger<ApiKeyService> logger)
    {
        this.session = session;
        this.logger = logger;

        var mapping = new MappingConfiguration().Define(
            new Map<ApiKey>()
                .TableName("api_keys")
                .PartitionKey(k => k.Key)
                .Column(k => k.Key, cm => cm.WithName("key"))
                .Column(k => k.UserId, cm => cm.WithName("user_id").WithSecondaryIndex())
                .Column(k => k.MinecraftUuid, cm => cm.WithName("minecraft_uuid"))
                .Column(k => k.ProfileId, cm => cm.WithName("profile_id"))
                .Column(k => k.MinecraftName, cm => cm.WithName("minecraft_name"))
                .Column(k => k.CreatedAt, cm => cm.WithName("created_at"))
                .Column(k => k.IsActive, cm => cm.WithName("is_active"))
                .Column(k => k.LastUsed, cm => cm.WithName("last_used"))
                .Column(k => k.UsageCount, cm => cm.WithName("usage_count"))
        );

        apiKeyTable = new Table<ApiKey>(session, mapping);

        try
        {
            // If there's no keyspace set on the session we can't check; log and continue to create table.
            var keyspace = session.Keyspace;
            if (string.IsNullOrEmpty(keyspace))
            {
                logger.LogWarning("Session has no keyspace set; skipping existence check and continuing.");
            }
            else
            {
                var prepared = session.Prepare("SELECT table_name FROM system_schema.tables WHERE keyspace_name = ? AND table_name = ?");
                var rs = session.Execute(prepared.Bind(keyspace, "api_keys"));
                var enumerator = rs.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    logger.LogInformation("Table 'api_keys' already exists in keyspace {Keyspace}; skipping creation.", keyspace);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            // If the check fails for any reason, log and continue with creation to avoid leaving the application in an unknown state.
            logger.LogWarning(ex, "Failed to check existing tables; proceeding to create table.");
        }

        // Create table if not exists with TTL of 90 days
        session.Execute("CREATE TABLE IF NOT EXISTS api_keys ("
            + "key text, "
            + "user_id text, "
            + "minecraft_uuid text, "
            + "profile_id text, "
            + "minecraft_name text, "
            + "created_at timestamp, "
            + "is_active boolean, "
            + "last_used timestamp, "
            + "usage_count bigint, "
            + "PRIMARY KEY (key)) "
            + "WITH default_time_to_live = 15552000"); // 180 days

        // Ensure secondary index on user_id for querying by user
        session.Execute("CREATE INDEX IF NOT EXISTS ON api_keys (user_id)");
    }

    /// <summary>
    /// Generates a new API key for a user
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="minecraftUuid">The Minecraft UUID</param>
    /// <param name="profileId">The profile ID</param>
    /// <param name="minecraftName">The Minecraft name</param>
    /// <returns>The generated API key</returns>
    public async Task<string> GenerateApiKey(string userId, string minecraftUuid, string profileId, string minecraftName)
    {
        try
        {
            // Generate a secure API key
            var key = GenerateSecureKey();

            var apiKey = new ApiKey
            {
                Key = key,
                UserId = userId,
                MinecraftUuid = minecraftUuid,
                ProfileId = profileId,
                MinecraftName = minecraftName,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                LastUsed = null,
                UsageCount = 0
            };

            await apiKeyTable.Insert(apiKey).ExecuteAsync();

            logger.LogInformation($"Generated API key for user {userId} with Minecraft UUID {minecraftUuid}");
            return key;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to generate API key for user {userId}");
            throw;
        }
    }

    /// <summary>
    /// Retrieves API key information by key
    /// </summary>
    /// <param name="key">The API key</param>
    /// <returns>The API key information or null if not found</returns>
    public async Task<ApiKey?> GetApiKeyInfo(string key)
    {
        try
        {
            var result = await apiKeyTable.Where(k => k.Key == key).FirstOrDefault().ExecuteAsync();
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to retrieve API key info for key {key.Substring(0, Math.Min(8, key.Length))}...");
            throw;
        }
    }

    /// <summary>
    /// Updates the last used timestamp and increments usage count for an API key
    /// </summary>
    /// <param name="key">The API key</param>
    public async Task UpdateKeyUsage(ApiKey key)
    {
        try
        {
            if (key.CreatedAt.AddDays(180) >= DateTime.UtcNow)
                return;
            await apiKeyTable.Where(k => k.Key == key.Key)
                                    .Select(k => new ApiKey { LastUsed = DateTime.UtcNow, UsageCount = key.UsageCount + 1 })
                                    .Update().ExecuteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to update usage for API key {key.Key.Truncate(8)}...");
            // Don't throw here as this is a non-critical operation
        }
    }

    /// <summary>
    /// Deactivates an API key
    /// </summary>
    /// <param name="key">The API key to deactivate</param>
    public async Task DeactivateApiKey(string key)
    {
        try
        {
            await apiKeyTable.Where(k => k.Key == key)
                .Select(k => new ApiKey { IsActive = false })
                .Update().ExecuteAsync();

            logger.LogInformation($"Deactivated API key {key.Substring(0, Math.Min(8, key.Length))}...");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to deactivate API key {key.Substring(0, Math.Min(8, key.Length))}...");
            throw;
        }
    }

    /// <summary>
    /// Gets all API keys for a user
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <returns>List of API keys for the user</returns>
    public async Task<IEnumerable<ApiKey>> GetUserApiKeys(string userId)
    {
        try
        {
            // Note: This requires a secondary index on user_id which should be created separately
            var result = await apiKeyTable.Where(k => k.UserId == userId).ExecuteAsync();
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to retrieve API keys for user {userId}");
            throw;
        }
    }

    /// <summary>
    /// Generates a cryptographically secure API key
    /// </summary>
    /// <returns>A secure API key string</returns>
    private static string GenerateSecureKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32]; // 256 bits
        rng.GetBytes(bytes);

        // Convert to base64 and make it URL-safe
        var key = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return $"sk_{key}"; // Add prefix to identify it as a SkyMod API key
    }
}

/// <summary>
/// Represents an API key in the database
/// </summary>
public class ApiKey
{
    public string Key { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string MinecraftUuid { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string MinecraftName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastUsed { get; set; }
    public long UsageCount { get; set; }
}
