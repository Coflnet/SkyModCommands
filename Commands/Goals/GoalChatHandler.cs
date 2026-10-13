using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Items.Client.Api;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Completes the Sell step of a goal from the chat line that confirms a listing or a bazaar sell offer.
/// Never throws, a failing goal update must not affect the rest of the chat processing.
/// </summary>
public class GoalChatHandler
{
    /// <summary>Whether a chat line may confirm a listing or sale.</summary>
    public static bool IsCandidate(string line)
        => line.StartsWith("BIN Auction started") || line.StartsWith("Auction started for")
        || line.StartsWith("[Bazaar] Sell Offer Setup!") || line.StartsWith("[Bazaar] Sold ");

    /// <summary>Applies the line to the goals of the user and saves them when a step progressed.</summary>
    public async Task Handle(MinecraftSocket socket, string line)
    {
        try
        {
            var userId = socket.UserId;
            if (userId == null || !GoalChatProgress.TryParse(line, out _, out _, out _))
                return;
            var list = await LoadGoals(socket, userId);
            if (list?.Goals == null || !list.Goals.Any(g => g.Root?.Kind == StepKind.Sell && g.Root.DoneAt == null))
                return;
            var names = await GetNames(socket);
            if (GoalChatProgress.Apply(list, line, tag => tag != null && names.TryGetValue(tag, out var name) ? name : null, DateTime.UtcNow))
                await SaveGoals(socket, userId, list);
        }
        catch (NoLoginException)
        {
            // not logged in, no goals to update
        }
        catch (Exception e)
        {
            Console.WriteLine("goal chat progress failed " + e.Message);
        }
    }

    /// <summary>Loads the goals of the user.</summary>
    protected virtual async Task<GoalList> LoadGoals(MinecraftSocket socket, string userId)
        => await socket.GetService<SettingsService>().GetCurrentValue(userId, GoalCommand.SettingsKey, () => new GoalList());

    /// <summary>Saves the goals of the user.</summary>
    protected virtual Task SaveGoals(MinecraftSocket socket, string userId, GoalList list)
        => socket.GetService<SettingsService>().UpdateSetting(userId, GoalCommand.SettingsKey, list);

    /// <summary>Item names by tag.</summary>
    protected virtual async Task<Dictionary<string, string>> GetNames(MinecraftSocket socket)
    {
        var names = await socket.GetService<IItemsApi>().ItemNamesGetAsync() ?? new();
        return names.GroupBy(n => n.Tag).ToDictionary(g => g.Key, g => g.First().Name);
    }
}
