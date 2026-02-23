
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

    public async Task TickForward(TimeSpan span)
    {
        var targetTime = Now + span;
        while (true)
        {
            var existingTasks = Tasks.OrderBy(t=>t.Value).ToList();
            var task = existingTasks.FirstOrDefault(t => t.Value <= targetTime);
            if (task.Key == null) break;

            Tasks.TryRemove(task.Key, out _);
            task.Key.SetResult();
            await Task.Delay(1); // Yield to allow continuations to run
        }
        Now += span;
    }
}
