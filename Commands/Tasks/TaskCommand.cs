using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC.Tasks;

[CommandDescription(
    "Lists tasks that can be done for profit",
    "Tasks are calculated based on your current progress",
    "and try to self adjust based on how many items",
    "you managed to collect recently (active tasks)",
    "Passive tasks include flips from other commands")]
public class TaskCommand : ReadOnlyListCommand<TaskResult>
{
    private ClassNameDictonary<ProfitTask> _tasks = TaskCatalog.Create();
    private ConcurrentDictionary<Type, TaskParams.CalculationCache> Cache = CreateCalculationCache();

    public TaskCommand()
    {
    }
    public override bool IsPublic => true;

    protected override void Format(MinecraftSocket socket, DialogBuilder db, TaskResult elem)
    {
        var typeTag = elem.Type switch
        {
            TaskType.Passive => $"{McColorCodes.DARK_AQUA}[Passive] ",
            TaskType.Limited => $"{McColorCodes.GOLD}[Limited] ",
            _ => ""
        };
        var accessTag = !elem.IsAccessible ? $"{McColorCodes.DARK_GRAY}[Unavailable] " : "";
        db.MsgLine($"§6{socket.FormatPrice(elem.ProfitPerHour)} /h {accessTag}{typeTag}{McColorCodes.GRAY}{elem.Message}", elem.OnClick, BuildListHover(elem));
    }

    protected override async Task<IEnumerable<TaskResult>> GetElements(MinecraftSocket socket, string val)
    {
        var parameters = await BuildParameters(socket, Cache);
        var all = await Task.WhenAll(_tasks.Select(async t =>
        {
            try
            {
                var result = await t.Value.Execute(parameters);
                result.Name ??= t.Value.Name;
                return PrepareTaskResult(result);
            }
            catch (Exception e)
            {
                return PrepareTaskResult(new TaskResult
                {
                    ProfitPerHour = 0,
                    Message = $"§cError while trying to calculate task {t.Key} {t.Value.Description}",
                    Details = e.ToString(),
                    Name = t.Value.Name
                });
            }
        }).ToList());
        // Sort: accessible tasks first by profit, inaccessible at the end
        return all
            .OrderBy(r => r.IsAccessible ? 0 : 1)
            .ThenByDescending(r => r.ProfitPerHour)
            .ToList();
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<TaskResult> elements, IEnumerable<TaskResult> toDisplay)
    {
        db.MsgLine("Please let us know if any of the numbers are incorrect on discord", "/cofl report numbers incorrect", "For larger bugs you will usually be rewarded as well\nClick to get a report reference id!");
        if (socket.Version.StartsWith("1.5") || socket.Version.StartsWith("1.6"))
            db.MsgLine($"{McColorCodes.RED}There is a newer mod version that improves this feature");
    }

    protected override string GetId(TaskResult elem)
    {
        return elem.ProfitPerHour + elem.Message;
    }

    internal static ConcurrentDictionary<Type, TaskParams.CalculationCache> CreateCalculationCache()
    {
        return new ConcurrentDictionary<Type, TaskParams.CalculationCache>();
    }

    internal static async Task<TaskParams> BuildParameters(MinecraftSocket socket, ConcurrentDictionary<Type, TaskParams.CalculationCache> cache)
    {
        var itemsApi = socket.GetService<Items.Client.Api.IItemsApi>();
        var cleanPrices = socket.GetService<ISniperClient>().GetCleanPrices();
        var bazaarPrices = socket.GetService<IBazaarApi>().GetAllPricesAsync();
        var locationProfitTask = socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdProfitHistoryGetAsync(socket.SessionInfo.McUuid, DateTime.UtcNow, 300);
        var namesTask = itemsApi.ItemNamesGetWithHttpInfoAsync();
        var extractedState = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        var locationProfit = await locationProfitTask;
        var names = JsonConvert.DeserializeObject<List<Items.Client.Model.ItemPreview>>((await namesTask).RawContent);
        var nameLookup = names?.ToDictionary(i => i.Tag, i => i.Name) ?? [];
        if (nameLookup.Count == 0)
        {
            socket.SendMessage($"{MinecraftSocket.COFLNET}{McColorCodes.RED}Could not get item names, using tags instead");
        }

        var taskService = socket.GetService<TaskService>();
        var locationProfitData = locationProfit.Where(d => d.EndTime - d.StartTime < TimeSpan.FromHours(1)).GroupBy(l => l.Location)?.ToDictionary(l => l.Key, l => l.ToArray()) ?? [];

        taskService.UpdateGlobalAverages(locationProfitData);

        return new TaskParams
        {
            TestTime = DateTime.UtcNow,
            ExtractedInfo = extractedState,
            Socket = socket,
            Formatter = new MinecraftSocketFormatProvider(socket),
            Cache = cache,
            CleanPrices = await cleanPrices,
            BazaarPrices = await bazaarPrices,
            Names = nameLookup,
            LocationProfit = locationProfitData,
            MaxAvailableCoins = socket.SessionInfo.Purse > 0 ? socket.SessionInfo.Purse : 1000000000,
            CurrentMayor = socket.GetService<FilterStateService>()?.State?.CurrentMayor?.ToLowerInvariant(),
            GlobalAverageDrops = taskService.GetGlobalAverages()
        };
    }

    internal static TaskResult PrepareTaskResult(TaskResult result)
    {
        result.Name ??= "Unknown Task";
        if (string.IsNullOrWhiteSpace(result.PrimaryAction))
            result.PrimaryAction = result.OnClick;
        result.OnClick = $"/cofl taskdetails {result.Name}";
        return result;
    }

    internal static string BuildListHover(TaskResult elem)
    {
        var details = elem.Details ?? "";
        if (elem.Breakdown != null)
        {
            var b = elem.Breakdown;
            if (!string.IsNullOrEmpty(b.HowTo))
                details += $"\n\n{McColorCodes.GREEN}How to: {McColorCodes.GRAY}{b.HowTo}";
            if (!string.IsNullOrEmpty(b.Category))
                details += $"\n{McColorCodes.YELLOW}Category: {McColorCodes.GRAY}{b.Category}";
            if (b.RequiredItems?.Count > 0)
                details += $"\n{McColorCodes.YELLOW}Required: {McColorCodes.GRAY}" + string.Join(", ", b.RequiredItems.Select(r => r.Name ?? r.ItemTag));
            if (!string.IsNullOrEmpty(elem.InaccessibleReason))
                details += $"\n{McColorCodes.RED}{elem.InaccessibleReason}";
            if (elem.NextAvailableAt.HasValue)
                details += $"\n{McColorCodes.YELLOW}Next available: {McColorCodes.GRAY}{TaskDetailsCommand.FormatAbsoluteTime(elem.NextAvailableAt.Value)} ({TaskDetailsCommand.FormatRelativeTime(elem.NextAvailableAt.Value - DateTime.UtcNow)})";
        }
        if (!string.IsNullOrWhiteSpace(details))
            details += "\n\n";
        details += $"{McColorCodes.AQUA}Click for full task details";
        return details;
    }
}
