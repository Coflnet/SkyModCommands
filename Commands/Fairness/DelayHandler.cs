using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using System.Threading;

namespace Coflnet.Sky.Commands.MC;

public interface IDelayHandler
{
    event Action<TimeSpan> OnDelayChange;
    TimeSpan CurrentDelay { get; }
    Task<DateTime> AwaitDelayForFlip(FlipInstance flipInstance);
    bool IsLikelyBot(FlipInstance flipInstance);
    Task<DelayHandler.Summary> Update(IEnumerable<string> ids, DateTime lastCaptchaSolveTime);
}

/// <summary>
/// Handles fairness delays to balance flips amongst all users
/// </summary>
public class DelayHandler : IDelayHandler
{
    private static readonly TimeSpan AntiAfkDelay = TimeSpan.FromSeconds(12);
    private int FlipIndex = 0;
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxSuperPremiumDelay = TimeSpan.FromMilliseconds(700);
    public static readonly double DelayReduction = 0.3;


    private readonly ITimeProvider timeProvider;
    public TimeSpan CurrentDelay => currentDelay;
    private TimeSpan currentDelay = DefaultDelay;
    private TimeSpan macroPenalty = TimeSpan.Zero;
    private SessionInfo sessionInfo;
    private Random random;
    private SelfUpdatingValue<AccountInfo> accountInfo;
    private FlipTrackingService flipTrackingService;
    public event Action<TimeSpan> OnDelayChange;

    public DelayHandler(ITimeProvider timeProvider, FlipTrackingService flipTrackingService, SessionInfo sessionInfo, SelfUpdatingValue<AccountInfo> accountInfo, Random random = null)
    {
        this.timeProvider = timeProvider;
        this.random = random;
        if (random == null)
            this.random = Random.Shared;
        this.flipTrackingService = flipTrackingService;
        this.sessionInfo = sessionInfo;
        this.accountInfo = accountInfo;
    }

    public async Task<DateTime> AwaitDelayForFlip(FlipInstance flipInstance)
    {
        if (currentDelay <= TimeSpan.Zero)
            return timeProvider.Now;
        if (IsLikelyBot(flipInstance))
            return timeProvider.Now;
        if (flipInstance.Profit < 200_000 && flipInstance.Finder == Core.LowPricedAuction.FinderType.FLIPPER)
            return timeProvider.Now;
        var myIndex = FlipIndex;
        Interlocked.Increment(ref FlipIndex);
        TimeSpan delay = GetCorrectDelay(myIndex);
        var part1 = delay / 4;
        var part2 = delay - part1;
        await timeProvider.Delay(part1).ConfigureAwait(false);
        var time = timeProvider.Now;
        await timeProvider.Delay(part2).ConfigureAwait(false);
        var apiBed = flipInstance.Auction.Start > timeProvider.Now - TimeSpan.FromSeconds(20) && !(flipInstance.Auction.Context?.ContainsKey("pre-api") ?? true);
        var isHighProfit = flipInstance.Profit > 5_000_000 || flipInstance.Finder == Core.LowPricedAuction.FinderType.SNIPER && flipInstance.Profit > 2_500_000;
        if (sessionInfo.IsMacroBot && flipInstance.Profit > 1_000_000)
            await timeProvider.Delay(TimeSpan.FromMicroseconds(flipInstance.Profit / 20000)).ConfigureAwait(false);
        if (isHighProfit && (!apiBed || random.NextDouble() < 0.5))
            await timeProvider.Delay(macroPenalty).ConfigureAwait(false);
        return time;
    }

    /// <summary>
    /// flips likely to get bottet have a chance of no delay 
    /// </summary>
    /// <param name="flipInstance"></param>
    /// <returns></returns>
    public bool IsLikelyBot(FlipInstance flipInstance)
    {
        if (currentDelay == AntiAfkDelay)
            return false; // afk users don't get instant flips

        // 30% chance of no delay so lowest ping macro doesn't get a huge advantage
        if (random.NextDouble() > 0.7)
            return false;

        var tag = flipInstance.Auction?.Tag;
        var profit = flipInstance.ProfitPercentage;
        var profitSum = flipInstance.Profit;
        if(profitSum > 50_000_000)
            return true;
        return tag != null && (
                    (tag.Contains("DIVAN") || tag == "FROZEN_SCYTHE" || tag.StartsWith("SORROW_")
                    || tag.StartsWith("NECROMANCER_LORD_") || tag.Contains("ASPECT")
                    || tag.EndsWith("SHADOW_FURY") || tag == "HYPERION")
                        && profit > 100
                    || (tag.Contains("CRIMSON") || tag.StartsWith("POWER_WITHER_")
                        || tag == "BAT_WAND" || tag == "DWARF_TURTLE_SHELMET" || tag == "JUJU_SHORTBOW"
                        || tag.Contains("GEMSTONE") || tag.StartsWith("FINAL_DESTINATION"))
                        && profit > 200)
                    || profit > 900;
    }

    public async Task<Summary> Update(IEnumerable<string> ids, DateTime lastCaptchaSolveTime)
    {
        var filteredIds = ids.Where(i => !string.IsNullOrEmpty(i)).ToArray();
        if (filteredIds.Length == 0)
            return new Summary() { Penalty = TimeSpan.FromSeconds(2.5) };
        var breakdown = await flipTrackingService.GetSpeedComp(filteredIds);
        var hourCount = breakdown?.Times?.Where(t => t.TotalSeconds > 1).GroupBy(t => TimeSpan.Parse(t.Age).Hours).Count() ?? 0;
        var recommendedPenalty = breakdown?.Penalty ?? 2;
        var lastDelay = currentDelay;
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

        if (HasFlippedForLong(lastCaptchaSolveTime, hourCount) && breakdown.BoughtWorth > 80_000_000)
        {
            summary.AntiAfk = true;
            if (lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromHours(3))
                currentDelay = AntiAfkDelay;
        }
        else if (recommendedPenalty > 0.8 && lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromMinutes(118) && breakdown?.BoughtWorth > 40_000_000)
        {
            summary.AntiAfk = true;
            if (lastCaptchaSolveTime < timeProvider.Now - TimeSpan.FromMinutes(120))
                currentDelay = AntiAfkDelay;
        }
        if (breakdown?.MacroedFlips?.Where(m => m.BuyTime > DateTime.UtcNow - TimeSpan.FromHours(6)).Count() <= 2)
            macroPenalty = TimeSpan.Zero;
        else
        {
            macroPenalty = TimeSpan.FromSeconds(1);
            if (breakdown?.MacroedFlips != null && breakdown.MacroedFlips.Max(f => f.BuyTime) > DateTime.UtcNow - TimeSpan.FromSeconds(180))
                summary.MacroWarning = true;
        }
        if (accountInfo?.Value?.Tier >= AccountTier.SUPER_PREMIUM)
        {
            currentDelay *= (1 - DelayReduction);
            if (currentDelay > MaxSuperPremiumDelay)
                currentDelay = MaxSuperPremiumDelay;
            macroPenalty *= (1 - DelayReduction);
            if (!breakdown.Buys.Values.Any(b => b > accountInfo.Value.ExpiresAt - TimeSpan.FromMinutes(4)))
            {
                currentDelay = TimeSpan.Zero;
                macroPenalty = TimeSpan.Zero;
            }
        }
        if (currentDelay != lastDelay)
            OnDelayChange?.Invoke(currentDelay);
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

