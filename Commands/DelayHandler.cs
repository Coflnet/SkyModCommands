using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using System.Threading;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Handles fairness delays to balance flips amongst all users
/// </summary>
public class DelayHandler
{
    private int FlipIndex = 0;
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(2);

    private readonly ITimeProvider timeProvider;
    internal TimeSpan CurrentDelay => currentDelay;
    private TimeSpan currentDelay = DefaultDelay;
    private SessionInfo sessionInfo;
    private Random random;
    private FlipTrackingService flipTrackingService;

    public DelayHandler(ITimeProvider timeProvider, FlipTrackingService flipTrackingService, SessionInfo sessionInfo, Random random = null)
    {
        this.timeProvider = timeProvider;
        this.random = random;
        if (random == null)
            this.random = Random.Shared;
        this.flipTrackingService = flipTrackingService;
        this.sessionInfo = sessionInfo;
    }

    public async Task AwaitDelayForFlip()
    {
        if (currentDelay <= TimeSpan.Zero)
            return;
        var myIndex = FlipIndex;
        Interlocked.Increment(ref FlipIndex);
        TimeSpan delay = GetCorrectDelay(myIndex);

        await timeProvider.Delay(delay);
    }

    public async Task<Summary> Update(IEnumerable<string> ids, DateTime lastCaptchaSolveTime)
    {
        var breakdown = await flipTrackingService.GetSpeedComp(ids);
        var hourCount = breakdown?.Times?.Where(t => t.TotalSeconds > 1).GroupBy(t => System.TimeSpan.Parse(t.Age).Hours).Count() ?? 0;
        currentDelay = TimeSpan.FromSeconds(breakdown.Penalty);

        var summary = new Summary();

        if (currentDelay > TimeSpan.Zero || !sessionInfo.VerifiedMc)
        {
            //span = tracer.BuildSpan("nerv").AsChildOf(ConSpan).StartActive();
            //span.Span.SetTag("time", currentDelay.ToString());
            //span.Span.Log(JsonConvert.SerializeObject(ids, Formatting.Indented));
        }

        if (!sessionInfo.VerifiedMc)
            currentDelay += TimeSpan.FromSeconds(3);
        else
            summary.VerifiedMc = true;

        if (hourCount > 3 && lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromHours(1.4))
        {
            summary.AntiMacro = true;
            if (lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromHours(1.5))
            {
                currentDelay += TimeSpan.FromSeconds(12);
            }
        }
        summary.Penalty = currentDelay;

        return summary;
    }

    public class Summary
    {
        public TimeSpan Penalty { get; set; }
        public bool AntiMacro;
        public bool VerifiedMc;
    }

    private TimeSpan GetCorrectDelay(int myIndex)
    {
        // this user is malicious, delay fully
        if (currentDelay >= TimeSpan.FromSeconds(2))
            return currentDelay;
        return myIndex switch
        {
            0 => currentDelay,
            1 => currentDelay - random.NextDouble() * currentDelay * 0.75,
            2 => currentDelay / 2 - random.NextDouble() * currentDelay / 2,
            3 => currentDelay / 3 - random.NextDouble() * currentDelay / 3,
            _ => currentDelay
        };
    }
}

