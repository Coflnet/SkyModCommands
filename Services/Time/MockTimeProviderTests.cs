using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class MockTimeProviderTests
{
    [Test]
    public async Task Test()
    {
        var timeProvider = new MockTimeProvider();
        var task = timeProvider.Delay(TimeSpan.FromSeconds(1));
        
        // tick forward before await so it return immediately
        timeProvider.TickForward(TimeSpan.FromSeconds(1));
        var sw = new Stopwatch();
        await task;
        // less than 1% of time has actually passed
        Assert.Greater(0.01, sw.Elapsed.TotalSeconds);
    }
}