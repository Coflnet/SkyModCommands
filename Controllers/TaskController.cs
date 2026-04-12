using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.MC.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TaskController : ControllerBase
{
    private readonly TaskService _taskService;
    private readonly ActivityTrackingService _activityService;
    private readonly IPlayerStateApi _playerStateApi;
    private readonly IBazaarApi _bazaarApi;
    private readonly ISniperClient _sniperClient;
    private readonly Items.Client.Api.IItemsApi _itemsApi;
    private readonly IServiceProvider _serviceProvider;

    public TaskController(
        TaskService taskService,
        ActivityTrackingService activityService,
        IPlayerStateApi playerStateApi,
        IBazaarApi bazaarApi,
        ISniperClient sniperClient,
        Items.Client.Api.IItemsApi itemsApi,
        IServiceProvider serviceProvider)
    {
        _taskService = taskService;
        _activityService = activityService;
        _playerStateApi = playerStateApi;
        _bazaarApi = bazaarApi;
        _sniperClient = sniperClient;
        _itemsApi = itemsApi;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Get all money-making task results for a player, sorted by profit/hour.
    /// </summary>
    [HttpGet("{playerId}")]
    public async Task<List<TaskResult>> GetTaskResults(string playerId)
    {
        var cleanPrices = _sniperClient.GetCleanPrices();
        var bazaarPrices = _bazaarApi.GetAllPricesAsync();
        var locationProfitTask = _playerStateApi.PlayerStatePlayerIdProfitHistoryGetAsync(playerId, DateTime.UtcNow, 300);
        var namesTask = _itemsApi.ItemNamesGetWithHttpInfoAsync();
        var extractedState = await _playerStateApi.PlayerStatePlayerIdExtractedGetAsync(playerId);
        var locationProfit = await locationProfitTask;
        var names = JsonConvert.DeserializeObject<List<Items.Client.Model.ItemPreview>>((await namesTask).RawContent);
        var nameLookup = names?.ToDictionary(i => i.Tag, i => i.Name) ?? [];

        var parameters = new TaskParams
        {
            TestTime = DateTime.UtcNow,
            ExtractedInfo = extractedState,
            Formatter = new SimpleTaskFormatProvider(),
            Cache = new ConcurrentDictionary<Type, TaskParams.CalculationCache>(),
            CleanPrices = await cleanPrices,
            BazaarPrices = await bazaarPrices,
            Names = nameLookup,
            LocationProfit = locationProfit
                .Where(d => d.EndTime - d.StartTime < TimeSpan.FromHours(1))
                .GroupBy(l => l.Location)
                .ToDictionary(l => l.Key, l => l.ToArray()),
            MaxAvailableCoins = 1_000_000_000,
            GlobalAverageDrops = _taskService.GetGlobalAverages(),
            ServiceProvider = _serviceProvider,
            PlayerUuid = playerId,
            PlayerName = playerId
        };

        // Contribute this player's data to community averages
        _taskService.UpdateGlobalAverages(parameters.LocationProfit);

        return await _taskService.ExecuteAll(parameters);
    }

    /// <summary>
    /// Get metadata for all registered money-making methods (no player data needed).
    /// </summary>
    [HttpGet("methods")]
    public List<MethodMetadata> GetMethods()
    {
        return _taskService.GetMethodMetadata();
    }

    // ── Activity tracking ──

    /// <summary>
    /// Set a player's current activity (what method they are doing).
    /// </summary>
    [HttpPost("activity")]
    public IActionResult SetActivity([FromBody] SetActivityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.PlayerId) || string.IsNullOrWhiteSpace(request?.MethodName))
            return BadRequest("PlayerId and MethodName are required.");
        _activityService.SetActivity(request.PlayerId, request.MethodName, request.Location);
        return Ok();
    }

    /// <summary>
    /// Clear a player's current activity.
    /// </summary>
    [HttpDelete("activity/{playerId}")]
    public IActionResult ClearActivity(string playerId)
    {
        _activityService.ClearActivity(playerId);
        return Ok();
    }

    /// <summary>
    /// Get all players currently doing a specific method.
    /// </summary>
    [HttpGet("activity/{methodName}/players")]
    public List<PlayerActivity> GetPlayersDoingMethod(string methodName)
    {
        return _activityService.GetPlayersDoingMethod(methodName);
    }

    /// <summary>
    /// Get count of active players per method.
    /// </summary>
    [HttpGet("activity/counts")]
    public Dictionary<string, int> GetActivityCounts()
    {
        return _activityService.GetActivePlayerCounts();
    }
}

public class SetActivityRequest
{
    public string PlayerId { get; set; }
    public string MethodName { get; set; }
    public string Location { get; set; }
}
