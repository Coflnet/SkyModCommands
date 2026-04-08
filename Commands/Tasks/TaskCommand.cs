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

    public override async Task Execute(MinecraftSocket socket, string args)
    {
        socket.SendMessage($"{MinecraftSocket.COFLNET}{McColorCodes.GRAY}Loading tasks... this can take a few seconds.");
        await base.Execute(socket, args);
    }

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
                return PrepareTaskResult(result, t.Value.Name);
            }
            catch (Exception e)
            {
                return PrepareTaskResult(new TaskResult
                {
                    ProfitPerHour = 0,
                    Message = $"§cError while trying to calculate task {t.Key} {t.Value.Description}",
                    Details = e.ToString(),
                    Name = t.Value.Name
                }, t.Value.Name);
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
            if (!elem.IsAccessible && !string.IsNullOrWhiteSpace(elem.InaccessibleReason))
                lines.Add($"{McColorCodes.RED}{elem.InaccessibleReason}");
            else if (elem.NextAvailableAt.HasValue)
                lines.Add($"{McColorCodes.YELLOW}Next available: {McColorCodes.GRAY}{TaskDetailsCommand.FormatRelativeTime(elem.NextAvailableAt.Value - DateTime.UtcNow)}");
        }
        lines.Add($"{McColorCodes.AQUA}Click for full task details");
        return string.Join("\n", lines);
    }
}
