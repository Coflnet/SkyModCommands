
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.Shared;

public class MockTimeProvider : ITimeProvider
{
    public DateTime Now { get; set; } = DateTime.Now;
    public ConcurrentDictionary<TaskCompletionSource, DateTime> Tasks { get; set; } = new();

    public Task Delay(TimeSpan timeSpan)
    {
        var tcs = new TaskCompletionSource();
        Tasks.TryAdd(tcs, Now + timeSpan);
        return tcs.Task;
    }

    public void TickForward(TimeSpan span)
    {
        Now += span;
        foreach (var item in Tasks.ToList())
        {
            if (item.Value <= Now)
            {
                item.Key.SetResult();
                Tasks.TryRemove(item.Key, out _);
            }
        }
    }
}
