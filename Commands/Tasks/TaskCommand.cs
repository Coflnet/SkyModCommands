using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC.Tasks;

[CommandDescription(
    "Lists tasks that can be done for profit",
    "Tasks are calculated based on your current progress",
    "and try to self adjust based on how many items",
    "you managed to collect recently (active tasks)",
    "Passive tasks include flips from other commands")]
public class TaskCommand : ReadOnlyListCommand<TaskResult>
{
    private ClassNameDictonary<ProfitTask> _tasks = new ClassNameDictonary<ProfitTask>();
    private ConcurrentDictionary<Type, TaskParams.CalculationCache> Cache = new();

    public TaskCommand()
    {
        _tasks.Add<KatTask>();
        _tasks.Add<ForgeTask>();
        _tasks.Add<GalateaDivingTask>();
        _tasks.Add<GalateaFishingTask>();
        _tasks.Add<GalateaTask>();
        _tasks.Add<JerryTask>();
    }
    public override bool IsPublic => true;

    protected override void Format(MinecraftSocket socket, DialogBuilder db, TaskResult elem)
    {
        db.MsgLine($"§6{socket.FormatPrice(elem.ProfitPerHour)} /h {McColorCodes.GRAY}{elem.Message}", elem.OnClick, elem.Details);
    }

    protected override async Task<IEnumerable<TaskResult>> GetElements(MinecraftSocket socket, string val)
    {
        var locationProfit = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdProfitHistoryGetAsync(socket.SessionInfo.McUuid, DateTime.UtcNow, 300);
        var extractedState = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        var parameters = new TaskParams
        {
            TestTime = DateTime.UtcNow,
            ExtractedInfo = extractedState,
            Socket = socket,
            Cache = Cache,
            LocationProfit = locationProfit.GroupBy(l=>l.Location).ToDictionary(l => l.Key, l => l.ToArray()),
            MaxAvailableCoins = socket.SessionInfo.Purse > 0 ? socket.SessionInfo.Purse : 1000000000 // Default to 1 billion coins if not set
        };
        var all = await Task.WhenAll(_tasks.Select(async t =>
        {
            try
            {
                return await t.Value.Execute(parameters);
            }
            catch (Exception e)
            {
                return new TaskResult
                {
                    ProfitPerHour = 0,
                    Message = $"§cError while trying to calculate task {t.Key} {t.Value.Description}",
                    Details = e.ToString()
                };
            }
        }).ToList());
        return all.OrderByDescending(r => r.ProfitPerHour).ToList();
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<TaskResult> elements, IEnumerable<TaskResult> toDisplay)
    {
        if (!socket.Version.StartsWith("1.6.3"))
            db.MsgLine($"{McColorCodes.RED}Active tasks require at least mod version 1.6.3 to work properly");
    }

    protected override string GetId(TaskResult elem)
    {
        return elem.ProfitPerHour + elem.Message;
    }
}
