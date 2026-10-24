using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Items.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Track a goal like crafting or forging an item",
    "/cofl goal list - show your goals",
    "/cofl goal bazaar <itemTag> <quantity> - guide a buy-order to sell-offer flip",
    "/cofl goal add <itemTag> [quantity] [sell] [forge] - track a new goal",
    "/cofl goal focus|remove|reset <id> - manage a goal",
    "/cofl goal clear - remove all goals")]
public class GoalCommand : McCommand
{
    public const string SettingsKey = "goals";
    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        await socket.SessionInfo.BazaarDisplayLock.WaitAsync();
        try { await ExecuteLocked(socket, arguments); }
        finally { socket.SessionInfo.BazaarDisplayLock.Release(); }
    }

    private async Task ExecuteLocked(MinecraftSocket socket, string arguments)
    {
        var userId = socket.UserId;
        if (userId == null)
        {
            await socket.SendLoginPrompt();
            return;
        }
        var input = (Convert<string>(arguments) ?? "").Trim();
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sub = parts.Length == 0 ? "list" : parts[0].ToLowerInvariant();
        var list = await LoadGoals(socket, userId) ?? new GoalList();
        list.Goals ??= new();
        var id = parts.Length > 1 ? parts[1] : null;

        switch (sub)
        {
            case "list":
                ShowList(socket, list);
                return;
            case "focus":
                if (FindGoal(socket, list, id) == null)
                    return;
                list.FocusId = list.Goals.First(g => g.Id == id).Id;
                await SaveGoals(socket, userId, list);
                socket.Dialog(db => db.MsgLine("Focused goal " + list.Goals.First(g => g.Id == id).Title));
                return;
            case "remove":
                var toRemove = FindGoal(socket, list, id);
                if (toRemove == null)
                    return;
                list.Goals.Remove(toRemove);
                if (list.FocusId == toRemove.Id)
                    list.FocusId = list.Goals.FirstOrDefault()?.Id;
                await SaveGoals(socket, userId, list);
                socket.Dialog(db => db.MsgLine($"Removed goal {toRemove.Title}"));
                return;
            case "reset":
                var toReset = FindGoal(socket, list, id);
                if (toReset == null)
                    return;
                if (toReset.BazaarFlip != null && list.Goals.Any(g => g != toReset && g.RootItemTag == toReset.RootItemTag
                    && g.BazaarFlip != null && BazaarFlipProgress.Next(g) != BazaarFlipAction.Done))
                {
                    socket.Dialog(db => db.MsgLine("You already have an active Bazaar flip for this item. Focus or remove it first."));
                    return;
                }
                GoalProgressEvaluator.Reset(toReset);
                await SaveGoals(socket, userId, list);
                socket.Dialog(db => db.MsgLine($"Reset progress of {toReset.Title}"));
                return;
            case "clear":
                list.Goals.Clear();
                list.FocusId = null;
                await SaveGoals(socket, userId, list);
                socket.Dialog(db => db.MsgLine("Removed all goals"));
                return;
            case "bazaar":
                if (parts.Length != 3 || !long.TryParse(parts[2], out var amount) || amount <= 0 || amount > int.MaxValue)
                {
                    socket.Dialog(db => db.MsgLine("Usage: /cofl goal bazaar <itemTag> <quantity>"));
                    return;
                }
                var tag = parts[1].ToUpperInvariant();
                if (!(await GetBazaarTags(socket)).Contains(tag))
                {
                    socket.Dialog(db => db.MsgLine("That item tag is not traded on the Bazaar."));
                    return;
                }
                if (HasActiveBazaarFlip(list, tag))
                {
                    socket.Dialog(db => db.MsgLine("You already have an active Bazaar flip for this item. Focus or remove it first."));
                    return;
                }
                var names = await GetNames(socket);
                var created = DateTime.UtcNow;
                created = new DateTime(created.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
                var flip = new Goal { CreatedAt = created, RootItemTag = tag, Quantity = amount, Title = $"Bazaar flip: {names.GetValueOrDefault(tag, tag)} ×{amount}",
                    Source = "mod", BazaarFlip = new(), Root = new GoalStep { Kind = StepKind.Sell, ItemTag = tag, Quantity = amount } };
                list.Goals.Add(flip);
                list.FocusId = flip.Id;
                await SaveGoals(socket, userId, list);
                socket.Dialog(db => db.MsgLine($"Tracking {flip.Title}. Open your inventory to show the guide. New orders and final claims are tracked; existing holdings do not complete this flip."));
                return;
            case "add":
                await Add(socket, userId, list, parts.Skip(1).ToArray());
                return;
            default:
                socket.Dialog(db => db.MsgLine($"Unknown goal command `{sub}`, use list, add, bazaar, focus, remove, reset or clear"));
                return;
        }
    }

    /// <summary>True when the list holds an unfinished bazaar flip goal for the item; only one may exist per item.</summary>
    public static bool HasActiveBazaarFlip(GoalList list, string tag)
        => list?.Goals?.Any(g => g.RootItemTag == tag && g.BazaarFlip != null && BazaarFlipProgress.Next(g) != BazaarFlipAction.Done) ?? false;

    private async Task Add(MinecraftSocket socket, string userId, GoalList list, string[] args)
    {
        if (args.Length == 0)
        {
            socket.Dialog(db => db.MsgLine("Usage: /cofl goal add <itemTag> [quantity] [sell] [forge]"));
            return;
        }
        var tag = args[0].ToUpperInvariant();
        long quantity = 1;
        var sell = false;
        var forge = false;
        foreach (var arg in args.Skip(1))
        {
            if (long.TryParse(arg, out var q) && q > 0)
                quantity = q;
            else if (arg.Equals("sell", StringComparison.OrdinalIgnoreCase))
                sell = true;
            else if (arg.Equals("forge", StringComparison.OrdinalIgnoreCase))
                forge = true;
        }
        var bazaar = await GetBazaarTags(socket);
        var names = await GetNames(socket);
        var planner = new GoalPlanner(socket.GetService<ICraftsApi>(), socket.GetService<IForgeApi>());
        var goal = await planner.Build(forge ? StepKind.Forge : StepKind.Craft, tag, quantity, sell, bazaar,
            t => names.GetValueOrDefault(t, t), "mod");
        list.Goals.Add(goal);
        list.FocusId ??= goal.Id;
        await SaveGoals(socket, userId, list);
        socket.Dialog(db => db.MsgLine($"Tracking {goal.Title}: open your inventory to see the steps"));
    }

    private static Goal FindGoal(MinecraftSocket socket, GoalList list, string id)
    {
        var goal = id == null ? null : list.Goals.FirstOrDefault(g => g.Id == id);
        if (goal == null)
            socket.Dialog(db => db.MsgLine("Goal not found, use /cofl goal list to see your goals"));
        return goal;
    }

    private static void ShowList(MinecraftSocket socket, GoalList list)
    {
        if (list.Goals.Count == 0)
        {
            socket.Dialog(db => db.MsgLine("You have no goals, add one with /cofl goal add <itemTag> [quantity]"));
            return;
        }
        var counts = new Dictionary<string, long>();
        foreach (var item in socket.SessionInfo?.Inventory ?? new())
        {
            if (item?.Tag == null)
                continue;
            counts[item.Tag] = counts.GetValueOrDefault(item.Tag) + Math.Max(1, item.Count);
        }
        var empty = new Dictionary<string, long>();
        socket.Dialog(db =>
        {
            foreach (var goal in list.Goals)
            {
                var progress = GoalProgressEvaluator.Evaluate(goal, counts, empty, new HashSet<string>(), DateTime.UtcNow);
                var prefix = goal.Id == list.FocusId ? "§6★ " : "§7▸ ";
                db.MsgLine($"{prefix}{goal.Title} {progress.Done}/{progress.Total}", $"/cofl goal focus {goal.Id}",
                    $"Click to focus\n/cofl goal remove {goal.Id} to remove\n/cofl goal reset {goal.Id} to reset progress");
            }
            return db;
        });
    }

    protected virtual async Task<GoalList> LoadGoals(MinecraftSocket socket, string userId)
    {
        return await socket.GetService<SettingsService>().GetCurrentValue(userId, SettingsKey, () => new GoalList());
    }

    protected virtual Task SaveGoals(MinecraftSocket socket, string userId, GoalList list)
    {
        return socket.GetService<SettingsService>().UpdateSetting(userId, SettingsKey, list);
    }

    protected virtual async Task<ISet<string>> GetBazaarTags(MinecraftSocket socket)
    {
        return new HashSet<string>(await socket.GetService<IItemsApi>().ItemsBazaarTagsGetAsync() ?? new());
    }

    protected virtual async Task<Dictionary<string, string>> GetNames(MinecraftSocket socket)
    {
        var names = await socket.GetService<IItemsApi>().ItemNamesGetAsync() ?? new();
        return names.GroupBy(n => n.Tag).ToDictionary(g => g.Key, g => g.First().Name);
    }
}
