using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

public interface IMuseumDonationService
{
    Task ProcessChatMessage(string playerId, string message);
    HashSet<string> GetRecentDonations(string playerId);
    void ClearOldDonations();
}

public class MuseumDonationService : IMuseumDonationService
{
    private readonly ISearchApi _searchApi;
    private readonly ILogger<MuseumDonationService> _logger;
    
    // Store recent donations per player with timestamp for cleanup
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTime>> _recentDonations = new();
    
    // Regex pattern to match museum donation messages
    private static readonly Regex DonationPattern = new(@"You donated your (.+?) to the Museum! \+(\d+) SkyBlock XP", RegexOptions.Compiled);
    
    // Keep donations for 20 minutes since API updates within ~5 minutes and players might connect to different instances
    private static readonly TimeSpan DonationRetentionTime = TimeSpan.FromMinutes(20);

    public MuseumDonationService(ISearchApi searchApi, ILogger<MuseumDonationService> logger)
    {
        _searchApi = searchApi;
        _logger = logger;
    }

    public async Task ProcessChatMessage(string playerId, string message)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(message))
            return;

        var match = DonationPattern.Match(message);
        if (!match.Success)
            return;

        var itemName = match.Groups[1].Value;
        var expGained = match.Groups[2].Value;

        try
        {
            // Use the search API to find the correct item tag
            var searchResult = await _searchApi.ApiItemSearchSearchValGetAsync(itemName);
            
            if (searchResult?.FirstOrDefault() != null)
            {
                var itemTag = searchResult.First().Id;
                AddDonatedItem(playerId, itemTag);
                
                _logger.LogInformation("Museum donation recorded: Player {PlayerId} donated {ItemName} (tag: {ItemTag}) for {ExpGained} XP", 
                    playerId, itemName, itemTag, expGained);
            }
            else
            {
                _logger.LogWarning("Could not find item tag for museum donation: Player {PlayerId} donated {ItemName}", 
                    playerId, itemName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing museum donation for player {PlayerId}, item {ItemName}", 
                playerId, itemName);
        }
    }

    public HashSet<string> GetRecentDonations(string playerId)
    {
        if (string.IsNullOrEmpty(playerId) || !_recentDonations.TryGetValue(playerId, out var playerDonations))
        {
            return new HashSet<string>();
        }

        var cutoffTime = DateTime.UtcNow - DonationRetentionTime;
        var recentItems = new HashSet<string>();

        foreach (var donation in playerDonations.ToList())
        {
            if (donation.Value >= cutoffTime)
            {
                recentItems.Add(donation.Key);
            }
            else
            {
                // Remove expired donations
                playerDonations.TryRemove(donation.Key, out _);
            }
        }

        return recentItems;
    }

    public void ClearOldDonations()
    {
        var cutoffTime = DateTime.UtcNow - DonationRetentionTime;
        
        foreach (var playerEntry in _recentDonations.ToList())
        {
            var playerDonations = playerEntry.Value;
            var expiredItems = playerDonations
                .Where(d => d.Value < cutoffTime)
                .Select(d => d.Key)
                .ToList();

            foreach (var expiredItem in expiredItems)
            {
                playerDonations.TryRemove(expiredItem, out _);
            }

            // Remove player entry if no donations remain
            if (playerDonations.IsEmpty)
            {
                _recentDonations.TryRemove(playerEntry.Key, out _);
            }
        }

        _logger.LogDebug("Cleaned up old museum donations. Current player count: {PlayerCount}", _recentDonations.Count);
    }

    private void AddDonatedItem(string playerId, string itemTag)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(itemTag))
            return;

        var playerDonations = _recentDonations.GetOrAdd(playerId, _ => new ConcurrentDictionary<string, DateTime>());
        playerDonations.AddOrUpdate(itemTag, DateTime.UtcNow, (key, oldValue) => DateTime.UtcNow);
    }
}
