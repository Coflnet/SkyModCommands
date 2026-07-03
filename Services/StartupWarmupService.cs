using System;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

#nullable enable

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Shared flag describing whether the process has warmed up its critical downstream
/// dependencies. The readiness probe reads it through <see cref="WarmupHealthCheck"/> so
/// kubernetes only routes mod connections to a pod once those dependencies are hot.
///
/// Without this gate, the first connections after a deploy pay the full cold-start cost
/// inline. A trace of such a connection showed setup taking ~18s instead of &lt;2s, with a
/// 5s redis <c>SUBSCRIBE</c> timeout and repeated http connection-pool wait timeouts
/// (<c>TaskCanceledException</c> in <c>WaitForConnectionWithTelemetryAsync</c>) while the
/// pod established its first sockets to redis and sky-settings.
/// </summary>
public class WarmupState
{
    /// <summary>True once warmup finished (either successfully or after giving up).</summary>
    public volatile bool IsReady;
}

/// <summary>
/// Reports the pod as ready only after <see cref="StartupWarmupService"/> has warmed the
/// critical downstream dependencies. Wired to the kubernetes readiness probe.
/// </summary>
public class WarmupHealthCheck : IHealthCheck
{
    private readonly WarmupState state;

    public WarmupHealthCheck(WarmupState state)
    {
        this.state = state;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(state.IsReady
            ? HealthCheckResult.Healthy("dependencies warmed")
            : HealthCheckResult.Unhealthy("warming up dependencies"));
    }
}

/// <summary>
/// Establishes connections to the dependencies that are exercised during connection setup
/// (the redis multiplexer incl. its subscription connection and the sky-settings http pool)
/// before the pod reports ready, so real connections no longer eat the cold-start latency.
/// </summary>
public class StartupWarmupService : IHostedService
{
    private readonly WarmupState state;
    private readonly IServiceProvider services;
    private readonly ILogger<StartupWarmupService> logger;

    /// <summary>
    /// Upper bound on how long we keep retrying before letting traffic in anyway. A pod that
    /// can never reach a dependency should still eventually become ready (degraded is better
    /// than being permanently out of rotation and failing the deploy); the readiness probe's
    /// initialDelay already covers the common warm-up window.
    /// </summary>
    private static readonly TimeSpan MaxWarmupTime = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    public StartupWarmupService(WarmupState state, IServiceProvider services, ILogger<StartupWarmupService> logger)
    {
        this.state = state;
        this.services = services;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // run in the background so we never block host startup (the metrics/liveness endpoint
        // must come up promptly); readiness stays false until WarmupAsync flips the flag.
        _ = Task.Run(() => WarmupAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task WarmupAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + MaxWarmupTime;
        var redisWarm = false;
        var settingsWarm = false;
        var attempts = 0;
        try
        {
            while (!(redisWarm && settingsWarm) && DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                attempts++;
                if (!redisWarm)
                    redisWarm = await TryWarmRedis();
                if (!settingsWarm)
                    settingsWarm = await TryWarmSettings();
                if (redisWarm && settingsWarm)
                    break;
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down before warmup completed; nothing to do
        }
        finally
        {
            state.IsReady = true;
        }

        if (redisWarm && settingsWarm)
            logger.LogInformation("startup warmup completed after {attempts} attempt(s)", attempts);
        else
            logger.LogWarning("startup warmup gave up after {attempts} attempt(s) (redis={redis}, settings={settings}); reporting ready anyway", attempts, redisWarm, settingsWarm);
    }

    private async Task<bool> TryWarmRedis()
    {
        try
        {
            var multiplexer = services.GetService<IConnectionMultiplexer>();
            if (multiplexer == null)
                return true; // not configured, nothing to warm
            await multiplexer.GetDatabase().PingAsync();
            // the cold-start failure in the trace was specifically on SUBSCRIBE, so exercise the
            // subscription connection with a throwaway sub/unsub round-trip to establish it too.
            var subscriber = multiplexer.GetSubscriber();
            var channel = RedisChannel.Literal("modcommands:warmup");
            await subscriber.SubscribeAsync(channel, (_, _) => { });
            await subscriber.UnsubscribeAsync(channel);
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "redis warmup attempt failed");
            return false;
        }
    }

    private async Task<bool> TryWarmSettings()
    {
        try
        {
            var settingsApi = services.GetService<ISettingsApi>();
            if (settingsApi == null)
                return true; // not configured, nothing to warm
            // cheap request purely to establish the tcp+tls connection pool to sky-settings;
            // the response value is irrelevant.
            await settingsApi.SettingsGetSettingAsync("0", "warmup");
            return true;
        }
        catch (Coflnet.Sky.Settings.Client.Client.ApiException)
        {
            // a non-2xx response still means the connection succeeded, so the pool is warm.
            return true;
        }
        catch (Exception e)
        {
            // connection-level failure (service not reachable yet) -> keep retrying.
            logger.LogWarning(e, "settings warmup attempt failed");
            return false;
        }
    }
}
