using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Service for managing youtuber data in Cassandra.
/// Handles table creation, saving new youtuber entries, and loading all known youtuber UUIDs.
/// </summary>
public class YoutuberService
{
    private readonly ISession session;
    private readonly ILogger<YoutuberService> logger;
    private Table<Youtuber> youtuberTable;
    private bool tableInitialized = false;

    public YoutuberService(ISession session, ILogger<YoutuberService> logger)
    {
        this.session = session;
        this.logger = logger;
    }

    /// <summary>
    /// Ensures the youtubers table is created and ready.
    /// </summary>
    private async Task EnsureTableAsync()
    {
        if (tableInitialized)
            return;

        try
        {
            var mapping = new MappingConfiguration().Define(
                new Map<Youtuber>()
                    .PartitionKey(u => u.NameLower)
                    .TableName("youtubers")
                    .Column(u => u.Name, cm => cm.WithName("name"))
                    .Column(u => u.Uuid, cm => cm.WithName("uuid"))
                    .Column(u => u.LastUpdated, cm => cm.WithName("last_updated"))
            );

            youtuberTable = new Table<Youtuber>(session, mapping);
            await youtuberTable.CreateIfNotExistsAsync();
            tableInitialized = true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not initialize youtubers table");
            throw;
        }
    }

    /// <summary>
    /// Loads all known youtuber UUIDs from the table.
    /// Returns a HashSet of unique UUIDs (case-insensitive).
    /// </summary>
    public async Task<HashSet<string>> LoadAllYoutuberUuidsAsync()
    {
        try
        {
            await EnsureTableAsync();
            var all = await youtuberTable.ExecuteAsync();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var y in all)
            {
                if (!string.IsNullOrWhiteSpace(y.Uuid))
                    set.Add(y.Uuid);
            }
            logger.LogInformation("Loaded {0} known youtubers from cassandra", set.Count);
            return set;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load youtubers from cassandra");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Checks if a youtuber with the given (lowercased) name already exists in the table.
    /// Returns null if not found.
    /// </summary>
    public async Task<Youtuber> GetYoutuberByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            await EnsureTableAsync();
            var nameLower = name.ToLowerInvariant();
            var existing = await youtuberTable.Where(y => y.NameLower == nameLower).FirstOrDefault().ExecuteAsync();
            return existing;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not query youtuber {name}", name);
            return null;
        }
    }

    /// <summary>
    /// Saves or updates a youtuber entry in the table.
    /// </summary>
    public async Task SaveYoutuberAsync(string name, string uuid)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uuid))
            return;

        try
        {
            await EnsureTableAsync();
            var record = new Youtuber
            {
                Name = name,
                NameLower = name.ToLowerInvariant(),
                Uuid = uuid,
                LastUpdated = DateTimeOffset.UtcNow
            };
            await youtuberTable.Insert(record).ExecuteAsync();
            logger.LogDebug("Saved youtuber {name} with uuid {uuid}", name, uuid);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not save youtuber {name}", name);
        }
    }
}
