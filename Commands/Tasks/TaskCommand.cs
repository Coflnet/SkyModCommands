using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.MC.Tasks;

[CommandDescription(
    "Lists tasks that can be done for profit",
    "Search by name, category or island, e.g. /cofl task mining",
    "Only tasks you have unlocked: /cofl task unlocked [search|page]",
    "(below starter premium the top 3 of that view are hidden)",
    "Tasks are calculated based on your current progress",
    "and try to self adjust based on how many items",
    "you managed to collect recently (active tasks)",
    "Passive tasks include flips from other commands")]
public class TaskCommand : ReadOnlyListCommand<TaskResult>
{
    public override bool IsPublic => true;
    protected override string Title => "Profitable tasks";
    private const int MaxCategoriesShown = 8;
    /// <summary>First argument word that switches to the unlocked-only view (also registered as no-op sorter so the base class strips it and still pages/searches the rest)</summary>
    internal const string UnlockedKeyword = "unlocked";
    internal const int HiddenTopCount = 3;
    internal const string HiddenTaskName = "Hidden task";
    internal const string UpgradeCommand = "/cofl buy starter_premium";
    internal const string UnlockedTitleSuffix = " (unlocked only)";

    public TaskCommand()
    {
        sorters = new Dictionary<string, System.Func<IEnumerable<TaskResult>, System.Linq.IOrderedEnumerable<TaskResult>>>(System.StringComparer.OrdinalIgnoreCase)
        {
            [UnlockedKeyword] = elements => elements.OrderBy(_ => 0)
        };
    }

    public override async Task Execute(MinecraftSocket socket, string args)
    {
        socket.SendMessage($"{MinecraftSocket.COFLNET}{McColorCodes.GRAY}Loading tasks... this can take a few seconds.");
        // the stats lookup runs while the tasks load so the player doesn't wait for both; it never throws
        var statsRefresh = StatsRefresh.RefreshIfDue(socket);
        await base.Execute(socket, args);
        await statsRefresh;
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, TaskResult elem)
    {
        if (IsHiddenPlaceholder(elem))
        {
            FormatHidden(db);
            return;
        }
        var typeTag = elem.Type switch
        {
            TaskType.NUMBER_1 => $"{McColorCodes.DARK_AQUA}[Passive] ",
            TaskType.NUMBER_2 => $"{McColorCodes.GOLD}[Limited] ",
            _ => ""
        };
        var accessTag = !elem.IsAccessible ? $"{McColorCodes.DARK_GRAY}[Unavailable] " : "";
        db.MsgLine($"§6{socket.FormatPrice(elem.ProfitPerHour)} /h {accessTag}{typeTag}{McColorCodes.GRAY}{elem.Message}", elem.OnClick, BuildListHover(elem));
    }

    protected override async Task<IEnumerable<TaskResult>> GetElements(MinecraftSocket socket, string val)
    {
        // SkyPlayerState's player state (skills, HOTM, purse, claimed task, ...) is keyed by
        // Minecraft NAME, not uuid - sending McUuid here used to silently return an empty state
        // instead of the player's own (see SkyPlayerState's TaskExecutionService.BuildParameters).
        var results = await socket.GetService<TaskService>().GetResults(socket.SessionInfo.McName);
        var prepared = results.Select(r => PrepareTaskResult(r, r.Name)).ToList();
        // the tier is only looked up for the unlocked view, the normal list never needs it
        var hasStarter = !IsUnlockedView(val) || socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.STARTER_PREMIUM);
        return BuildView(prepared, val, hasStarter);
    }

    protected override string GetTitle(string arguments)
    {
        return IsUnlockedView(arguments) ? Title + UnlockedTitleSuffix : Title;
    }

    /// <summary>Same header as the base class, but the next page link stays in the current view</summary>
    protected override DialogBuilder PrintResult(MinecraftSocket socket, string title, int page, IEnumerable<TaskResult> toDisplay, int totalPages)
    {
        return DialogBuilder.New
            .MsgLine($"{title} (page {page}/{totalPages})", NextPageCommand(Slug, title, page), $"Click to go to next page ({page + 1})")
            .ForEach(toDisplay, (db, elem, i) => Format(socket, db, elem));
    }

    /// <summary>The view is read back from the title because the header is built without the arguments</summary>
    internal static string NextPageCommand(string slug, string title, int page)
    {
        var view = title.EndsWith(UnlockedTitleSuffix) ? UnlockedKeyword + " " : "";
        return $"/cofl {slug} {view}{page + 1}";
    }

    /// <summary>True when the first argument word is the unlocked keyword (case insensitive)</summary>
    internal static bool IsUnlockedView(string arguments)
    {
        var first = arguments?.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.Equals(first, UnlockedKeyword, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The elements for the requested view: untouched for the normal list, accessible only (with the top
    /// entries hidden for non starter players) for the unlocked view
    /// </summary>
    internal static List<TaskResult> BuildView(List<TaskResult> tasks, string arguments, bool hasStarter)
    {
        if (!IsUnlockedView(arguments))
            return tasks;
        return HideTopForTier(tasks.Where(t => t.IsAccessible).ToList(), hasStarter);
    }

    /// <summary>
    /// Replaces the first <see cref="HiddenTopCount"/> entries with placeholders unless the tier is starter or above.
    /// With fewer entries all of them are hidden.
    /// </summary>
    internal static List<TaskResult> HideTopForTier(List<TaskResult> ordered, bool hasStarter)
    {
        if (hasStarter)
            return ordered;
        var hidden = System.Math.Min(HiddenTopCount, ordered.Count);
        var placeholders = Enumerable.Range(0, hidden).Select(_ => CreateHiddenPlaceholder());
        return placeholders.Concat(ordered.Skip(hidden)).ToList();
    }

    internal static TaskResult CreateHiddenPlaceholder()
    {
        return new TaskResult { Name = HiddenTaskName, Message = HiddenTaskName, IsAccessible = true, OnClick = UpgradeCommand };
    }

    internal static bool IsHiddenPlaceholder(TaskResult elem)
    {
        return elem.Name == HiddenTaskName && elem.OnClick == UpgradeCommand;
    }

    internal static void FormatHidden(DialogBuilder db)
    {
        db.MsgLine($"{McColorCodes.RED}Top {HiddenTopCount} require starter premium to see",
            UpgradeCommand,
            $"{McColorCodes.GRAY}You need starter premium or higher to see the top {HiddenTopCount} unlocked tasks\n{McColorCodes.YELLOW}Click to buy a tier");
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<TaskResult> elements, IEnumerable<TaskResult> toDisplay, IEnumerable<TaskResult> unfilteredElements)
    {
        db.MsgLine("Please let us know if any of the numbers are incorrect on discord", "/cofl report numbers incorrect", "For larger bugs you will usually be rewarded as well\nClick to get a report reference id!");
        if (socket.Version.StartsWith("1.5") || socket.Version.StartsWith("1.6"))
            db.MsgLine($"{McColorCodes.RED}There is a newer mod version that improves this feature");
        AddViewSwitch(db);
        AddCategorySummary(db, unfilteredElements);
    }

    internal static void AddViewSwitch(DialogBuilder db)
    {
        db.Msg($"{McColorCodes.YELLOW}View: ");
        db.Msg($"{McColorCodes.AQUA}all tasks", "/cofl task", "Show every task including unavailable ones");
        db.Msg($"{McColorCodes.DARK_GRAY} · ");
        db.Msg($"{McColorCodes.AQUA}only unlocked", $"/cofl task {UnlockedKeyword}", "Show only tasks you can do right now");
        db.LineBreak();
    }

    protected override void PrintHelp(MinecraftSocket socket)
    {
        base.PrintHelp(socket);
        socket.Dialog(db => db.MsgLine($"Only tasks you can do: {McColorCodes.AQUA}/cofl task {UnlockedKeyword}{McColorCodes.GRAY} (top {HiddenTopCount} need starter premium)"));
        socket.Dialog(db => db.MsgLine($"Search matches task name, category, island and location, e.g. {McColorCodes.AQUA}/cofl task mining{McColorCodes.GRAY} or {McColorCodes.AQUA}/cofl task galatea"));
    }

    protected override IEnumerable<TaskResult> Search(IEnumerable<TaskResult> elements, string query)
    {
        return SearchKeepingHidden(elements, query, GetId);
    }

    /// <summary>Hidden placeholders survive every search, so searching never drops the premium notice</summary>
    internal static IEnumerable<TaskResult> SearchKeepingHidden(IEnumerable<TaskResult> elements, string query, System.Func<TaskResult, string> getId)
    {
        var list = elements.ToList();
        var visible = list.Where(e => !IsHiddenPlaceholder(e));
        return list.Where(IsHiddenPlaceholder).Concat(SearchTasks(visible, query, getId));
    }

    /// <summary>
    /// An argument equal to a present category filters by that category only, otherwise every
    /// field is matched on its own (a match never spans two fields)
    /// </summary>
    internal static IEnumerable<TaskResult> SearchTasks(IEnumerable<TaskResult> elements, string query, System.Func<TaskResult, string> getId)
    {
        var term = query.Trim();
        var list = elements.ToList();
        var isCategory = list.Any(e => string.Equals(e.Breakdown?.Category, term, System.StringComparison.OrdinalIgnoreCase));
        if (isCategory)
            return list.Where(e => string.Equals(e.Breakdown?.Category, term, System.StringComparison.OrdinalIgnoreCase));
        return list.Where(e => SearchFields(e, getId(e)).Any(f => f.Contains(term, System.StringComparison.OrdinalIgnoreCase)));
    }

    internal static IEnumerable<string> SearchFields(TaskResult elem, string id)
    {
        return new[] { id, elem.Name, elem.Breakdown?.Category, elem.Breakdown?.Island, elem.Breakdown?.Where }
            .Where(s => !string.IsNullOrWhiteSpace(s));
    }

    /// <summary>Categories over all elements ordered by count (then name), empty ones skipped</summary>
    internal static List<(string Category, int Count)> CountCategories(IEnumerable<TaskResult> elements)
    {
        return elements.Select(e => e.Breakdown?.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .GroupBy(c => c)
            .Select(g => (Category: g.Key, Count: g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Category)
            .ToList();
    }

    internal static void AddCategorySummary(DialogBuilder db, IEnumerable<TaskResult> elements)
    {
        var categories = CountCategories(elements).Take(MaxCategoriesShown).ToList();
        if (categories.Count == 0)
            return;
        db.Msg($"{McColorCodes.YELLOW}Categories: ");
        for (int i = 0; i < categories.Count; i++)
        {
            var (category, count) = categories[i];
            var separator = i < categories.Count - 1 ? $"{McColorCodes.DARK_GRAY} · " : "";
            db.Msg($"{McColorCodes.AQUA}{category} {McColorCodes.GRAY}({count})", $"/cofl task {category}", $"Show only {category} tasks");
            db.Msg(separator);
        }
        db.LineBreak();
    }

    protected override string GetId(TaskResult elem)
    {
        return elem.ProfitPerHour + elem.Message;
    }

    /// <summary>
    /// Rewrites a task result's OnClick to drill into <c>/cofl taskdetails</c> and preserves the
    /// original click (e.g. a warp) as PrimaryAction so TaskDetailsCommand can still offer it.
    /// </summary>
    internal static TaskResult PrepareTaskResult(TaskResult result, string commandTaskName = null)
    {
        result.Name ??= "Unknown Task";
        if (string.IsNullOrWhiteSpace(result.PrimaryAction))
            result.PrimaryAction = result.OnClick;
        var targetName = string.IsNullOrWhiteSpace(commandTaskName) ? result.Name : commandTaskName;
        result.OnClick = $"/cofl taskdetails {targetName}";
        return result;
    }

    internal static string BuildListHover(TaskResult elem)
    {
        var lines = new List<string>();
        if (elem.Breakdown != null)
        {
            if (!string.IsNullOrWhiteSpace(elem.Breakdown.Category))
                lines.Add($"{McColorCodes.YELLOW}Category: {McColorCodes.GRAY}{elem.Breakdown.Category}");
            if (!string.IsNullOrWhiteSpace(elem.Breakdown.Where))
            {
                var islandPart = !string.IsNullOrWhiteSpace(elem.Breakdown.Island) && elem.Breakdown.Island != elem.Breakdown.Where
                    ? $" ({elem.Breakdown.Island})" : "";
                lines.Add($"{McColorCodes.YELLOW}Where: {McColorCodes.GRAY}{elem.Breakdown.Where}{islandPart}");
            }
            if (!elem.IsAccessible && !string.IsNullOrWhiteSpace(elem.InaccessibleReason))
                lines.Add($"{McColorCodes.RED}{elem.InaccessibleReason}");
            else if (elem.NextAvailableAt.HasValue)
                lines.Add($"{McColorCodes.YELLOW}Next available: {McColorCodes.GRAY}{TaskDetailsCommand.FormatRelativeTime(elem.NextAvailableAt.Value - System.DateTime.UtcNow)}");
            if (elem.Breakdown.Steps?.Count > 0)
            {
                lines.Add($"{McColorCodes.AQUA}How:");
                foreach (var step in elem.Breakdown.Steps.Take(3))
                    lines.Add($"{McColorCodes.GRAY}{step.Number}. {step.Text}");
            }
        }
        lines.Add($"{McColorCodes.AQUA}Click for step-by-step guide");
        return string.Join("\n", lines);
    }
}
