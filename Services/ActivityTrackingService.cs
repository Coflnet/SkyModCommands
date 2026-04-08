using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.ModCommands.Models;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Tracks which money-making methods players are currently doing.
/// Uses in-memory storage with automatic expiration.
/// </summary>
public class ActivityTrackingService
{
    private readonly ConcurrentDictionary<string, PlayerActivity> _activities = new();
    private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Set or update a player's current activity.
    /// </summary>
    public void SetActivity(string playerId, string methodName, string location = null)
    {
        _activities[playerId] = new PlayerActivity
        {
            PlayerId = playerId,
            MethodName = methodName,
            StartedAt = DateTime.UtcNow,
            Location = location
        };
    }

    /// <summary>
    /// Clear a player's current activity.
    /// </summary>
    public bool ClearActivity(string playerId)
    {
        return _activities.TryRemove(playerId, out _);
    }

    /// <summary>
    /// Get a player's current activity.
    /// </summary>
    public PlayerActivity GetActivity(string playerId)
    {
        if (_activities.TryGetValue(playerId, out var activity))
        {
            if (DateTime.UtcNow - activity.StartedAt > Expiration)
            {
                _activities.TryRemove(playerId, out _);
                return null;
            }
            return activity;
        }
        return null;
    }

    /// <summary>
    /// Get all players currently doing a specific method.
    /// </summary>
    public List<PlayerActivity> GetPlayersDoingMethod(string methodName)
    {
        CleanExpired();
        return _activities.Values
            .Where(a => string.Equals(a.MethodName, methodName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Get count of active players per method.
    /// </summary>
    public Dictionary<string, int> GetActivePlayerCounts()
    {
        CleanExpired();
        return _activities.Values
            .GroupBy(a => a.MethodName)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Get total number of active players.
    /// </summary>
    public int GetTotalActiveCount()
    {
        CleanExpired();
        return _activities.Count;
    }

    private void CleanExpired()
    {
        var cutoff = DateTime.UtcNow - Expiration;
        foreach (var kvp in _activities)
        {
            if (kvp.Value.StartedAt < cutoff)
                _activities.TryRemove(kvp.Key, out _);
        }
    }
}
