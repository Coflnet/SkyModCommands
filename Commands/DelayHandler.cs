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
    private static readonly TimeSpan AntiAfkDelay = TimeSpan.FromSeconds(12);
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
        // flips likely to get bottet have no delay 
        if (IsLikelyBot(flipInstance))
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
            await timeProvider.Delay(macroPenalty).ConfigureAwait(false);
        return time;
    }

    public bool IsLikelyBot(FlipInstance flipInstance)
    {
        if(currentDelay == AntiAfkDelay)
            return false; // afk users don't get instant flips

        var tag = flipInstance.Auction?.Tag;
        var profit = flipInstance.ProfitPercentage;
        return tag != null && (
                    (tag.Contains("DIVAN") || tag == "FROZEN_SCYTHE" || tag.StartsWith("SORROW_")
                    || tag.StartsWith("NECROMANCER_LORD_") || tag.Contains("ASPECT"))
                        && profit > 100
                    || (tag.Contains("CRIMSON")
                        || tag == "BAT_WAND" || tag == "DWARF_TURTLE_SHELMET" || tag == "JUJU_SHORTBOW"
                        || tag.Contains("GEMSTONE") || tag.StartsWith("FINAL_DESTINATION"))
                        && profit > 200)
                    || profit > 900;
    }

    public async Task<Summary> Update(IEnumerable<string> ids, DateTime lastCaptchaSolveTime)
    {
        var breakdown = await flipTrackingService.GetSpeedComp(ids);
        var hourCount = breakdown?.Times?.Where(t => t.TotalSeconds > 1).GroupBy(t => System.TimeSpan.Parse(t.Age).Hours).Count() ?? 0;
        var recommendedPenalty = breakdown?.Penalty ?? 2;
        currentDelay = TimeSpan.FromSeconds(recommendedPenalty);

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
            summary.AntiAfk = true;
            if (lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromHours(3))
                currentDelay = AntiAfkDelay;
        }
        else if (recommendedPenalty > 0.8 && lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromMinutes(118))
        {
            summary.AntiAfk = true;
            if (lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromMinutes(120))
                currentDelay = AntiAfkDelay;
        }
        if (breakdown?.MacroedFlips?.Count <= 2)
            macroPenalty = TimeSpan.Zero;
        else
        {
            macroPenalty = TimeSpan.FromSeconds(1);
            if (breakdown?.MacroedFlips != null && breakdown.MacroedFlips.Max(f => f.BuyTime) > DateTime.Now - TimeSpan.FromSeconds(180))
                summary.MacroWarning = true;
        }
        summary.Penalty = currentDelay;
        return summary;
    }

    private bool HasFlippedForLong(DateTime lastCaptchaSolveTime, int hourCount)
    {
        return hourCount > 3 && lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromHours(2.4);
    }

    public class Summary
    {
        public TimeSpan Penalty { get; set; }
        public bool AntiAfk;
        public bool MacroWarning;
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

