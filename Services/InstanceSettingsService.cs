using System;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Keeps flip settings of players that are not signed in, keyed by the id of their mod instance (session id)
/// </summary>
public interface IInstanceSettingsService
{
    /// <summary>
    /// Returns the settings stored for the mod instance or null when the instance did not opt in
    /// </summary>
    Task<FlipSettings> Get(string sessionId);
    /// <summary>
    /// Same as <see cref="Get"/> but throws when the read fails,
    /// for code that decides whether to overwrite the stored settings
    /// </summary>
    Task<FlipSettings> GetStrict(string sessionId);
    Task Save(string sessionId, FlipSettings settings);
    Task Delete(string sessionId);
    /// <summary>
    /// Returns the item lore settings stored for the mod instance or null when there are none.
    /// Also null when the read failed, only use it for display
    /// </summary>
    Task<DescriptionSetting> GetDescription(string sessionId);
    /// <summary>
    /// Same as <see cref="GetDescription"/> but throws when the read fails,
    /// for code that writes the result back
    /// </summary>
    Task<DescriptionSetting> GetDescriptionStrict(string sessionId);
    /// <summary>
    /// Replaces the lore settings of an opted-in instance, writes nothing when the instance has no settings row
    /// </summary>
    /// <returns>false when nothing was saved because the instance has no settings row (never opted in, expired or deleted)</returns>
    Task<bool> SaveDescription(string sessionId, DescriptionSetting setting);
    /// <summary>
    /// Rewrites the row of an opted-in instance when it was last written long ago, so the ttl counts from the last use.
    /// Writes nothing when there is no row or it was written recently
    /// </summary>
    Task RefreshTtl(string sessionId);
}

public class InstanceSettingsService : IInstanceSettingsService
{
    private static readonly int TtlSeconds = (int)TimeSpan.FromDays(30).TotalSeconds;
    internal static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(24);
    private readonly Table<InstanceSettings> table;
    private readonly ILogger<InstanceSettingsService> logger;

    public InstanceSettingsService(ISession session, ILogger<InstanceSettingsService> logger)
        : this(CreateTable(session), logger)
    {
        table.CreateIfNotExists();
        AddDescriptionColumn(session);
    }

    internal InstanceSettingsService(Table<InstanceSettings> table, ILogger<InstanceSettingsService> logger)
    {
        this.table = table;
        this.logger = logger;
    }

    /// <summary>
    /// The table exists in production without the description column
    /// </summary>
    private static void AddDescriptionColumn(ISession session)
    {
        try
        {
            session.Execute("ALTER TABLE mod_instance_settings ADD description text");
        }
        catch (Exception e) when (BlockedService.IsColumnExistsError(e))
        {
            // already migrated
        }
    }

    internal static Table<InstanceSettings> CreateTable(ISession session)
    {
        return new Table<InstanceSettings>(session, new MappingConfiguration().Define(
            new Map<InstanceSettings>()
                .PartitionKey(x => x.SessionId)
                .Column(x => x.SessionId, cm => cm.WithName("session_id"))
                .Column(x => x.Settings, cm => cm.WithName("settings"))
                .Column(x => x.DisableFlips, cm => cm.WithName("disable_flips"))
                .Column(x => x.UpdatedAt, cm => cm.WithName("updated_at"))
                .Column(x => x.Description, cm => cm.WithName("description"))
        ), "mod_instance_settings");
    }

    internal static CqlQuerySingleElement<InstanceSettings> SelectQuery(Table<InstanceSettings> table, string sessionId)
        => table.Where(x => x.SessionId == sessionId).FirstOrDefault();

    internal static CqlInsert<InstanceSettings> InsertQuery(Table<InstanceSettings> table, InstanceSettings row)
    {
        // always the full row so all cells share the same fresh TTL
        var insert = table.Insert(row, insertNulls: false);
        insert.SetTTL(TtlSeconds);
        return insert;
    }

    internal static CqlDelete DeleteQuery(Table<InstanceSettings> table, string sessionId)
        => table.Where(x => x.SessionId == sessionId).Delete();

    public async Task<FlipSettings> Get(string sessionId)
    {
        try
        {
            return await GetStrict(sessionId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error loading instance settings");
            return null;
        }
    }

    public async Task<FlipSettings> GetStrict(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;
        return FromRow(await SelectQuery(table, sessionId).ExecuteAsync());
    }

    public async Task RefreshTtl(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;
        var row = await SelectQuery(table, sessionId).ExecuteAsync();
        if (!NeedsRefresh(row, DateTime.UtcNow))
            return;
        row.UpdatedAt = DateTime.UtcNow;
        await InsertQuery(table, row).ExecuteAsync();
    }

    /// <summary>
    /// Only opted-in rows are refreshed, and only when the last write is old enough to matter for the ttl
    /// </summary>
    internal static bool NeedsRefresh(InstanceSettings row, DateTime now)
    {
        if (string.IsNullOrEmpty(row?.Settings))
            return false;
        return now - row.UpdatedAt >= RefreshAfter;
    }

    public async Task<DescriptionSetting> GetDescription(string sessionId)
    {
        try
        {
            return await GetDescriptionStrict(sessionId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error loading instance description");
            return null;
        }
    }

    public async Task<DescriptionSetting> GetDescriptionStrict(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;
        return DescriptionFromRow(await SelectQuery(table, sessionId).ExecuteAsync());
    }

    public async Task<bool> SaveDescription(string sessionId, DescriptionSetting setting)
    {
        if (string.IsNullOrEmpty(sessionId) || setting == null)
            return false;
        var row = await SelectQuery(table, sessionId).ExecuteAsync();
        // no row or no settings: the instance did not opt in or disabled it, do not recreate a ghost row
        if (string.IsNullOrEmpty(row?.Settings))
            return false;
        row.Description = JsonConvert.SerializeObject(setting);
        row.UpdatedAt = DateTime.UtcNow;
        await InsertQuery(table, row).ExecuteAsync();
        return true;
    }

    internal static DescriptionSetting DescriptionFromRow(InstanceSettings row)
    {
        // the row only counts when the instance opted in, which is what the settings column marks
        if (string.IsNullOrEmpty(row?.Settings) || string.IsNullOrEmpty(row.Description))
            return null;
        return JsonConvert.DeserializeObject<DescriptionSetting>(row.Description);
    }

    public async Task Save(string sessionId, FlipSettings settings)
    {
        if (string.IsNullOrEmpty(sessionId) || settings == null)
            return;
        // read the row first, rewriting it must keep the stored description
        var existing = await SelectQuery(table, sessionId).ExecuteAsync();
        await InsertQuery(table, ToRow(sessionId, settings, existing?.Description)).ExecuteAsync();
    }

    /// <summary>
    /// <see cref="FlipSettings.DisableFlips"/> is not part of the settings data contract,
    /// so it gets its own column to survive the round trip
    /// </summary>
    internal static InstanceSettings ToRow(string sessionId, FlipSettings settings, string description = null)
    {
        return new InstanceSettings
        {
            SessionId = sessionId,
            Settings = JsonConvert.SerializeObject(settings),
            DisableFlips = settings.DisableFlips,
            UpdatedAt = DateTime.UtcNow,
            Description = description
        };
    }

    internal static FlipSettings FromRow(InstanceSettings row)
    {
        if (string.IsNullOrEmpty(row?.Settings))
            return null;
        var settings = JsonConvert.DeserializeObject<FlipSettings>(row.Settings);
        settings.DisableFlips = row.DisableFlips;
        return settings;
    }

    public async Task Delete(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;
        await DeleteQuery(table, sessionId).ExecuteAsync();
    }

    public class InstanceSettings
    {
        public string SessionId { get; set; }
        public string Settings { get; set; }
        public bool DisableFlips { get; set; }
        public DateTime UpdatedAt { get; set; }
        /// <summary>
        /// <see cref="DescriptionSetting"/> as json, it has byte fields the driver can't map
        /// </summary>
        public string Description { get; set; }
    }
}
