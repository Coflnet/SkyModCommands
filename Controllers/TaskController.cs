using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.AspNetCore.Mvc;

namespace Coflnet.Sky.ModCommands.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TaskController : ControllerBase
{
    private readonly TaskService _taskService;
    private readonly ActivityTrackingService _activityService;

    public TaskController(TaskService taskService, ActivityTrackingService activityService)
    {
        _taskService = taskService;
        _activityService = activityService;
    }

    /// <summary>
    /// Get all money-making task results for a player, sorted by profit/hour. Forwards to
    /// SkyPlayerState, which now computes every task result itself (registry, estimates,
    /// MethodBreakdown/guidance) - this mod only renders them.
    /// </summary>
    [HttpGet("{playerId}")]
    public async Task<List<TaskResult>> GetTaskResults(string playerId, CancellationToken cancellationToken)
    {
        return await _taskService.GetResults(playerId, cancellationToken);
    }

    /// <summary>
    /// Get metadata for all registered money-making methods (no player data needed). Forwards to
    /// SkyPlayerState.
    /// </summary>
    [HttpGet("methods")]
    public async Task<List<MethodMetadata>> GetMethods(CancellationToken cancellationToken)
    {
        return await _taskService.GetMethodMetadata(cancellationToken);
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
