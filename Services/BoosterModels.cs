using System;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Models for handling Hypixel boosters from the proxy API
/// </summary>
/// 
/// <summary>
/// Response from the boosters API endpoint
/// </summary>
public class BoosterResponse
{
    public bool success { get; set; }
    public List<BoosterEntry> boosters { get; set; } = new();
    public BoosterState boosterState { get; set; }
}

/// <summary>
/// Individual booster entry
/// </summary>
public class BoosterEntry
{
    public string _id { get; set; }
    public string purchaserUuid { get; set; }
    public double amount { get; set; }
    public long originalLength { get; set; }
    public long length { get; set; }
    public int gameType { get; set; }
    public long dateActivated { get; set; }
    // The API sometimes returns `stacked` as an array of UUIDs or as boolean true.
    // Keep it as a Json token and handle both cases when reading.
    public Newtonsoft.Json.Linq.JToken stacked { get; set; }
}

/// <summary>
/// Booster state information
/// </summary>
public class BoosterState
{
    public bool decrementing { get; set; }
}

/// <summary>
/// Active booster info with converted gamemode
/// </summary>
public class ActiveBooster
{
    public string purchaserUuid { get; set; }
    public string purchaserName { get; set; }
    public string gamemode { get; set; }
    public long timeActivated { get; set; }
    public long timeExpires { get; set; }
}

/// <summary>
/// Maps Hypixel game type IDs to Sky gamemode names
/// </summary>
public static class GameTypeMapper
{
    // Supported gamemodes map from ID to gamemode name
    private static readonly Dictionary<int, string> GameTypeMap = new()
    {
        { 2, "quakecraft" },
        { 3, "walls" },
        { 4, "paintball" },
        { 5, "blitz" },          // SURVIVAL_GAMES -> Blitz Survival Games
        { 6, "tnt" },             // TNTGAMES -> TNT Games
        { 7, "vampirez" },
        { 13, "megawalls" },      // WALLS3 -> Mega Walls
        { 14, "arcade" },
        { 17, "arena" },
        { 20, "uhc" },            // UHC -> UHC Champions
        { 21, "cnc" },            // MCGO -> Cops and Crims
        { 23, "warlords" },       // BATTLEGROUND -> Warlords
        { 24, "smash" },          // SUPER_SMASH -> Smash Heroes
        { 25, "turfwars" },       // GINGERBREAD -> Turbo Kart Racers
        { 26, "housing" },
        { 51, "skywars" },
        { 52, "crazywall" },      // TRUE_COMBAT -> Crazy Walls
        { 54, "speed_uhc" },
        { 55, "skyclash" },
        { 56, "legacy" },         // PROTOTYPE -> Classic Games (for autotip)
        { 58, "bedwars" },
        { 59, "murdermystery" },
        { 60, "buildbattle" },
        { 61, "duels" },
        { 63, "skyblock" },
        { 64, "pit" },
        { 65, "replay" },
        { 67, "smp" },
        { 68, "woolwars" },       // WOOL_GAMES -> Wool Wars
    };

    /// <summary>
    /// Convert a Hypixel game type ID to a Sky gamemode name
    /// </summary>
    public static string GetGamemode(int gameTypeId)
    {
        return GameTypeMap.TryGetValue(gameTypeId, out var gamemode) 
            ? gamemode 
            : null;
    }

    /// <summary>
    /// Check if a game type is supported for autotip
    /// </summary>
    public static bool IsSupportedForAutotip(int gameTypeId)
    {
        var gamemode = GetGamemode(gameTypeId);
        return gamemode != null && AutotipService.SupportedGamemodes.Any(g => g == gamemode);
    }
}
