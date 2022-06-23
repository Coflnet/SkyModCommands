
using System;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.Shared;
public interface ITimeProvider
{
    DateTime Now { get; }
    Task Delay(TimeSpan timeSpan);
}

public class TimeProvider : ITimeProvider
{
    public DateTime Now => DateTime.UtcNow;
    public static ITimeProvider Instance { get; protected set; } = new TimeProvider();

    public Task Delay(TimeSpan timeSpan)
    {
        return Task.Delay(timeSpan);
    }
}
