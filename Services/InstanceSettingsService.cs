using System;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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
    Task Save(string sessionId, FlipSettings settings);
    Task Delete(string sessionId);
}

public class InstanceSettingsService : IInstanceSettingsService
{
    private static readonly int TtlSeconds = (int)TimeSpan.FromDays(365).TotalSeconds;
    private readonly Table<InstanceSettings> table;
    private readonly ILogger<InstanceSettingsService> logger;

    public InstanceSettingsService(ISession session, ILogger<InstanceSettingsService> logger)
    {
        table = new Table<InstanceSettings>(session, new MappingConfiguration().Define(
            new Map<InstanceSettings>()
                .PartitionKey(x => x.SessionId)
                .Column(x => x.SessionId, cm => cm.WithName("session_id"))
                .Column(x => x.Settings, cm => cm.WithName("settings"))
                .Column(x => x.DisableFlips, cm => cm.WithName("disable_flips"))
                .Column(x => x.UpdatedAt, cm => cm.WithName("updated_at"))
        ), "mod_instance_settings");
        table.CreateIfNotExists();
        this.logger = logger;
    }

    public async Task<FlipSettings> Get(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;
        try
        {
            var row = await table.Where(x => x.SessionId == sessionId).FirstOrDefault().ExecuteAsync();
            return FromRow(row);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error loading instance settings of {sessionId}", sessionId);
            return null;
        }
    }

    public async Task Save(string sessionId, FlipSettings settings)
    {
        if (string.IsNullOrEmpty(sessionId) || settings == null)
            return;
        await table.Insert(ToRow(sessionId, settings)).SetTTL(TtlSeconds).ExecuteAsync();
    }

    /// <summary>
    /// <see cref="FlipSettings.DisableFlips"/> is not part of the settings data contract,
    /// so it gets its own column to survive the round trip
    /// </summary>
    internal static InstanceSettings ToRow(string sessionId, FlipSettings settings)
    {
        return new InstanceSettings
        {
            SessionId = sessionId,
            Settings = JsonConvert.SerializeObject(settings),
            DisableFlips = settings.DisableFlips,
            UpdatedAt = DateTime.UtcNow
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
        await table.Where(x => x.SessionId == sessionId).Delete().ExecuteAsync();
    }

    public class InstanceSettings
    {
        public string SessionId { get; set; }
        public string Settings { get; set; }
        public bool DisableFlips { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
