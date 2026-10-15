using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Models;

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
    public async Task Handle(IMinecraftSocket socket, string line)
    {
        var gate = socket.SessionInfo.BazaarDisplayLock;
        await gate.WaitAsync();
        try
        {
            var userId = socket.UserId;
            if (userId == null || !GoalChatProgress.TryParse(line, out _, out _, out _))
                return;
            var list = await LoadGoals(socket, userId);
            if (list?.Goals == null || !list.Goals.Any(g => g.BazaarFlip == null && g.Root?.Kind == StepKind.Sell && g.Root.DoneAt == null))
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
        finally { gate.Release(); }
    }

    /// <summary>Confirmed personal snapshots drive fills, cancellation and partial claims, including reconnect restores.</summary>
    public async Task HandleOrders(IMinecraftSocket socket, BazaarOrderSnapshot snapshot)
    {
        try
        {
            var userId = socket.UserId;
            if (userId == null || snapshot.UserId != userId
                || snapshot.Revision < (socket.SessionInfo.BazaarDisplayState?.Revision ?? 0)) return;
            var list = await LoadGoals(socket, userId);
            if (list?.Goals == null) return;
            var changed = false;
            var ownOrders = snapshot.Orders.Where(o => string.Equals(o.PlayerName, socket.SessionInfo.McName, StringComparison.OrdinalIgnoreCase)).ToList();
            var pendingBuys = ownOrders.Where(o => !o.IsSell && ((!o.IsExpired && (o.Claimed ?? 0) < o.Amount) || o.Filled > (o.Claimed ?? 0)))
                .Select(o => o.ItemId).ToHashSet();
            bool ClearCancelledOrders(GoalStep step)
            {
                if (step == null) return false;
                var cleared = step.Kind == StepKind.BuyOrder && step.ItemTag != null && step.OrderedAt.HasValue
                    && !step.DoneAt.HasValue && !pendingBuys.Contains(step.ItemTag);
                if (cleared) step.OrderedAt = null;
                foreach (var child in step.Children) cleared |= ClearCancelledOrders(child);
                return cleared;
            }
            foreach (var goal in list.Goals.Where(g => g.BazaarFlip == null))
                changed |= ClearCancelledOrders(goal.Root);
            foreach (var goal in list.Goals.Where(g => g.BazaarFlip != null && BazaarFlipProgress.Next(g) != BazaarFlipAction.Done))
            {
                var orders = ownOrders.Where(o => o.ItemId == goal.RootItemTag)
                    .Select(o => new BazaarFlipOrder { CreatedAt = o.Timestamp, ClosedAt = o.ClosedAt, IsSell = o.IsSell, Amount = o.Amount,
                        Filled = o.IsEstimate == false ? o.Filled : 0, Claimed = o.Claimed ?? 0, Active = !o.IsExpired });
                changed |= BazaarFlipProgress.Observe(goal, orders, snapshot.Revision);
            }
            if (changed) await SaveGoals(socket, userId, list);
        }
        catch (Exception e) { Console.WriteLine("Bazaar goal snapshot failed " + e.Message); }
    }

    /// <summary>Loads the goals of the user.</summary>
    protected virtual async Task<GoalList> LoadGoals(IMinecraftSocket socket, string userId)
        => await socket.GetService<SettingsService>().GetCurrentValue(userId, GoalCommand.SettingsKey, () => new GoalList());

    /// <summary>Saves the goals of the user.</summary>
    protected virtual Task SaveGoals(IMinecraftSocket socket, string userId, GoalList list)
        => socket.GetService<SettingsService>().UpdateSetting(userId, GoalCommand.SettingsKey, list);

    /// <summary>Item names by tag.</summary>
    protected virtual async Task<Dictionary<string, string>> GetNames(IMinecraftSocket socket)
    {
        var names = await socket.GetService<IItemsApi>().ItemNamesGetAsync() ?? new();
        return names.GroupBy(n => n.Tag).ToDictionary(g => g.Key, g => g.First().Name);
    }
}
