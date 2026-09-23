using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Prometheus;

namespace Coflnet.Sky.ModCommands.Services;

public sealed class MemoryDiagnosticsService : BackgroundService
{
    private static readonly Gauge HeapSize = Metrics.CreateGauge(
        "sky_mod_gc_heap_size_bytes", "Managed heap size after the latest garbage collection");
    private static readonly Gauge Fragmented = Metrics.CreateGauge(
        "sky_mod_gc_fragmented_bytes", "Managed heap fragmentation after the latest garbage collection");
    private static readonly Gauge Committed = Metrics.CreateGauge(
        "sky_mod_gc_committed_bytes", "Memory committed for the managed heap");
    private static readonly Gauge MemoryLoad = Metrics.CreateGauge(
        "sky_mod_gc_memory_load_bytes", "Physical memory load observed by the latest garbage collection");
    private static readonly Gauge HighMemoryLoadThreshold = Metrics.CreateGauge(
        "sky_mod_gc_high_memory_load_threshold_bytes", "Physical memory load threshold used by the garbage collector");
    private static readonly Gauge TotalAvailableMemory = Metrics.CreateGauge(
        "sky_mod_gc_total_available_memory_bytes", "Memory available to the garbage collector, including the container limit");
    private static readonly Gauge PinnedObjects = Metrics.CreateGauge(
        "sky_mod_gc_pinned_objects", "Pinned objects reported by the latest garbage collection");
    private static readonly Gauge FinalizationPendingObjects = Metrics.CreateGauge(
        "sky_mod_gc_finalization_pending_objects", "Objects ready for finalization after the latest garbage collection");
    private static readonly Gauge PauseTimePercentage = Metrics.CreateGauge(
        "sky_mod_gc_pause_time_percentage", "Percentage of time paused for garbage collection so far");
    private static readonly Gauge GenerationSize = Metrics.CreateGauge(
        "sky_mod_gc_generation_size_bytes", "Managed heap generation size after the latest garbage collection", "generation");
    private static readonly Gauge GenerationFragmentation = Metrics.CreateGauge(
        "sky_mod_gc_generation_fragmented_bytes", "Managed heap generation fragmentation after the latest garbage collection", "generation");
    private static readonly Gauge ThreadPoolThreads = Metrics.CreateGauge(
        "sky_mod_threadpool_threads", "Current number of thread-pool threads");
    private static readonly Gauge ThreadPoolPendingWork = Metrics.CreateGauge(
        "sky_mod_threadpool_pending_work_items", "Work items currently queued to the thread pool");

    private static readonly string[] GenerationLabels = ["gen0", "gen1", "gen2", "loh", "poh"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            Record();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static void Record()
    {
        var info = GC.GetGCMemoryInfo();
        HeapSize.Set(info.HeapSizeBytes);
        Fragmented.Set(info.FragmentedBytes);
        Committed.Set(info.TotalCommittedBytes);
        MemoryLoad.Set(info.MemoryLoadBytes);
        HighMemoryLoadThreshold.Set(info.HighMemoryLoadThresholdBytes);
        TotalAvailableMemory.Set(info.TotalAvailableMemoryBytes);
        PinnedObjects.Set(info.PinnedObjectsCount);
        FinalizationPendingObjects.Set(info.FinalizationPendingCount);
        PauseTimePercentage.Set(info.PauseTimePercentage);
        ThreadPoolThreads.Set(ThreadPool.ThreadCount);
        ThreadPoolPendingWork.Set(ThreadPool.PendingWorkItemCount);

        var generations = info.GenerationInfo;
        for (var index = 0; index < generations.Length && index < GenerationLabels.Length; index++)
        {
            GenerationSize.WithLabels(GenerationLabels[index]).Set(generations[index].SizeAfterBytes);
            GenerationFragmentation.WithLabels(GenerationLabels[index]).Set(generations[index].FragmentationAfterBytes);
        }
    }
}
