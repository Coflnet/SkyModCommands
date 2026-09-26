using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Thin wrapper over SkyPlayerState's Task api - all task computation (registry, formula/player
/// data/community estimate blending, MethodBreakdown/guidance) now happens server side
/// (SkyPlayerState.Services.Tasks.TaskExecutionService); this mod only forwards the request and
/// renders the resulting client DTOs. Used by both the WebSocket TaskCommand/TaskDetailsCommand/
/// TaskClaimCommand and the REST Controllers/TaskController.
/// </summary>
public class TaskService
{
    private readonly ITaskApi taskApi;
    private readonly ILogger<TaskService> logger;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public TaskService(ITaskApi taskApi, ILogger<TaskService> logger)
    {
        this.taskApi = taskApi;
        this.logger = logger;
    }

    /// <summary>
    /// All money-making task results for a player, sorted accessible-first then by profit/hour, as
    /// computed by SkyPlayerState.
    /// </summary>
    /// <exception cref="CoflnetException">SkyPlayerState is unreachable or too slow (slug
    /// "playerstate_unavailable") - callers do not need their own try/catch, both the WebSocket
    /// command dispatcher (MinecraftSocket.InvokeCommand) and the REST error handler already render
    /// a friendly message for it.</exception>
    public Task<List<TaskResult>> GetResults(string playerId, CancellationToken cancellationToken = default)
        => Call(cts => taskApi.TaskPlayerIdResultsGetAsync(playerId, cancellationToken: cts.Token),
            "task results", playerId, cancellationToken);

    /// <summary>
    /// A single task's full result (message, accessibility, MethodBreakdown) for a player. Returns
    /// null when no task is registered under <paramref name="taskName"/> (SkyPlayerState 404s).
    /// </summary>
    public async Task<TaskResult> GetResult(string playerId, string taskName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Call(cts => taskApi.TaskPlayerIdResultsTaskNameGetAsync(playerId, taskName, cancellationToken: cts.Token),
                "task result", playerId, cancellationToken);
        }
        catch (PlayerState.Client.Client.ApiException e) when (e.ErrorCode == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Metadata (name, description) for every registered money-making method - no player data
    /// needed. Used to validate a claimed task name (see TaskClaimCommand) and for
    /// Controllers/TaskController's <c>/api/task/methods</c>.
    /// </summary>
    public Task<List<MethodMetadata>> GetMethodMetadata(CancellationToken cancellationToken = default)
        => Call(cts => taskApi.TaskMethodsGetAsync(cancellationToken: cts.Token), "task methods", null, cancellationToken);

    private async Task<T> Call<T>(Func<CancellationTokenSource, Task<T>> call, string what, string playerId,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);
        try
        {
            return await call(cts);
        }
        catch (PlayerState.Client.Client.ApiException)
        {
            throw;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "failed to load {what} from SkyPlayerState for {player}", what, playerId);
            throw new CoflnetException("playerstate_unavailable",
                "Could not calculate tasks right now, SkyPlayerState is unavailable. Please try again in a moment.");
        }
    }
}
