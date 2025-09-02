using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
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
        _tasks.Add<ComposterTask>();
        _tasks.Add<GalateaDivingTask>();
        _tasks.Add<GalateaFishingTask>();
        _tasks.Add<GalateaTask>();
        _tasks.Add<JerryTask>();
        _tasks.Add<GoldMineTask>();
        _tasks.Add<DeepCavernsTask>();
        _tasks.Add<DwarvenMinesMiningTask>();
        _tasks.Add<TheEndTask>();
        _tasks.Add<TheParkTask>();
        _tasks.Add<BackwaterBayouTask>();
        _tasks.Add<GardenTask>();
        _tasks.Add<CrimsonIsleTask>();

        //_tasks.Add<SlayerTask>();
    }
    public override bool IsPublic => true;

    protected override void Format(MinecraftSocket socket, DialogBuilder db, TaskResult elem)
    {
        db.MsgLine($"§6{socket.FormatPrice(elem.ProfitPerHour)} /h {McColorCodes.GRAY}{elem.Message}", elem.OnClick, elem.Details);
    }

    protected override async Task<IEnumerable<TaskResult>> GetElements(MinecraftSocket socket, string val)
    {
        var names = socket.GetService<Items.Client.Api.IItemsApi>();
        var cleanPrices = socket.GetService<ISniperClient>().GetCleanPrices();
        var bazaarPrices = socket.GetService<IBazaarApi>().GetAllPricesAsync();
        var locationProfitTask = socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdProfitHistoryGetAsync(socket.SessionInfo.McUuid, DateTime.UtcNow, 300);
        var extractedState = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        var locationProfit = await locationProfitTask;
        var parameters = new TaskParams
        {
            TestTime = DateTime.UtcNow,
            ExtractedInfo = extractedState,
            Socket = socket,
            Cache = Cache,
            CleanPrices = await cleanPrices,
            BazaarPrices = await bazaarPrices,
            Names = (await names.ItemNamesGetAsync()).ToDictionary(i => i.Tag, i => i.Name),
            LocationProfit = locationProfit.Where(d => d.EndTime - d.StartTime < TimeSpan.FromHours(1)).GroupBy(l=>l.Location).ToDictionary(l => l.Key, l => l.ToArray()),
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
        db.MsgLine("Please let us know if any of the numbers are incorrect on discord", "/cofl report numbers incorrect", "For larger bugs you will usually be rewarded as well\nClick to get a report reference id!");
        if (socket.Version.StartsWith("1.5") || socket.Version.StartsWith("1.6"))
            db.MsgLine($"{McColorCodes.RED}There is a newer mod version that improves this feature");
    }

    protected override string GetId(TaskResult elem)
    {
        return elem.ProfitPerHour + elem.Message;
    }
}
