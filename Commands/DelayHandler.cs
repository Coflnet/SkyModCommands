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
    private static readonly TimeSpan AntiMacroDelay = TimeSpan.FromSeconds(12);
    private int FlipIndex = 0;
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(2);

    private readonly ITimeProvider timeProvider;
    internal TimeSpan CurrentDelay => currentDelay;
    private TimeSpan currentDelay = DefaultDelay;
    private TimeSpan macroPenalty = TimeSpan.Zero;
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

    public async Task<DateTime> AwaitDelayForFlip(FlipInstance flipInstance)
    {
        if (currentDelay <= TimeSpan.Zero)
            return timeProvider.Now;
        var myIndex = FlipIndex;
        Interlocked.Increment(ref FlipIndex);
        TimeSpan delay = GetCorrectDelay(myIndex);
        var part1 = delay / 4;
        var part2 = delay - part1;
        await timeProvider.Delay(part1).ConfigureAwait(false);
        var time = timeProvider.Now;
        await timeProvider.Delay(part2).ConfigureAwait(false);
        if (flipInstance.Profit > 5_000_000 || flipInstance.Finder == Core.LowPricedAuction.FinderType.SNIPER && flipInstance.Profit > 2_500_000)
            await timeProvider.Delay(macroPenalty);
        return time;
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

        if (HasFlippedForLong(lastCaptchaSolveTime, hourCount))
        {
            summary.AntiMacro = true;
            if (lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromHours(1.5))
                currentDelay = AntiMacroDelay;
        }
        else if (breakdown.Penalty > 0.8 && lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromMinutes(28))
        {
            summary.AntiMacro = true;
            if (lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromMinutes(30))
                currentDelay = AntiMacroDelay;
        }
        if (breakdown.MacroedFlips?.Count == 0)
            macroPenalty = TimeSpan.Zero;
        else
            macroPenalty = TimeSpan.FromSeconds(1);
        summary.Penalty = currentDelay;
        return summary;
    }

    private bool HasFlippedForLong(DateTime lastCaptchaSolveTime, int hourCount)
    {
        return hourCount > 3 && lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromHours(1.4);
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

