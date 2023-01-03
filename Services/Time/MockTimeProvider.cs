
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.Shared;

public class MockTimeProvider : ITimeProvider
{
    public DateTime Now { get; set; } = DateTime.UtcNow;
    public ConcurrentDictionary<TaskCompletionSource, DateTime> Tasks { get; set; } = new();

    public Task Delay(TimeSpan timeSpan)
    {
        var tcs = new TaskCompletionSource();
        Tasks.TryAdd(tcs, Now + timeSpan);
        return tcs.Task;
    }

    public void TickForward(TimeSpan span)
    {
        var existingTasks = Tasks.OrderBy(t=>t.Value);
        while (existingTasks.Where(t=>t.Value <= Now + span).Any())
        {
            var task = existingTasks.First();
            Tasks.TryRemove(task.Key, out _);
            task.Key.SetResult();
        }
        Now += span;
        Console.WriteLine($"Time is now {Now : mm:ss\\.fff}");
    }
}
