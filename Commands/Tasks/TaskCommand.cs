using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class TaskCommand : ReadOnlyListCommand<TaskResult>
{
    private ClassNameDictonary<ProfitTask> _tasks = new ClassNameDictonary<ProfitTask>();
    private ConcurrentDictionary<Type, TaskParams.CalculationCache> Cache = new();

    public TaskCommand()
    {
        _tasks.Add<KatTask>();
        _tasks.Add<ForgeTask>();
    }
    public override bool IsPublic => true;

    protected override void Format(MinecraftSocket socket, DialogBuilder db, TaskResult elem)
    {
        db.MsgLine($"§6{elem.ProfitPerHour} coins/h {McColorCodes.GRAY}{elem.Message}", elem.OnClick, elem.Details);
    }

    protected override async Task<IEnumerable<TaskResult>> GetElements(MinecraftSocket socket, string val)
    {
        var extractedState = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        var parameters = new TaskParams
        {
            TestTime = DateTime.UtcNow,
            ExtractedInfo = extractedState,
            Socket = socket,
            Cache = Cache,
            MaxAvailableCoins = socket.SessionInfo.Purse > 0 ? socket.SessionInfo.Purse : 1000000000 // Default to 1 billion coins if not set
        };
        var all = await Task.WhenAll(_tasks.Select(t => t.Value.Execute(parameters)).ToList());
        return all.OrderByDescending(r => r.ProfitPerHour).ToList();
    }

    protected override string GetId(TaskResult elem)
    {
        return elem.ProfitPerHour + elem.Message;
    }
}
