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
    "Tasks are calculated based on your current progress",
    "and try to self adjust based on how many items",
    "you managed to collect recently (active tasks)",
    "Passive tasks include flips from other commands")]
public class TaskCommand : ReadOnlyListCommand<TaskResult>
{
    public override bool IsPublic => true;
    protected override string Title => "Profitable tasks";
    private const int MaxCategoriesShown = 8;

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
        return results.Select(r => PrepareTaskResult(r, r.Name)).ToList();
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<TaskResult> elements, IEnumerable<TaskResult> toDisplay, IEnumerable<TaskResult> unfilteredElements)
    {
        db.MsgLine("Please let us know if any of the numbers are incorrect on discord", "/cofl report numbers incorrect", "For larger bugs you will usually be rewarded as well\nClick to get a report reference id!");
        if (socket.Version.StartsWith("1.5") || socket.Version.StartsWith("1.6"))
            db.MsgLine($"{McColorCodes.RED}There is a newer mod version that improves this feature");
        AddCategorySummary(db, unfilteredElements);
    }

    protected override void PrintHelp(MinecraftSocket socket)
    {
        base.PrintHelp(socket);
        socket.Dialog(db => db.MsgLine($"Search matches task name, category, island and location, e.g. {McColorCodes.AQUA}/cofl task mining{McColorCodes.GRAY} or {McColorCodes.AQUA}/cofl task galatea"));
    }

    protected override IEnumerable<TaskResult> Search(IEnumerable<TaskResult> elements, string query)
    {
        return SearchTasks(elements, query, GetId);
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
