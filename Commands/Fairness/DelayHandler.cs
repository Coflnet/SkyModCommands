using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using System.Threading;
using System.Diagnostics;

namespace Coflnet.Sky.Commands.MC;

public interface IDelayHandler
{
    event Action<TimeSpan> OnDelayChange;
    TimeSpan CurrentDelay { get; }
    TimeSpan MacroDelay { get; }
    Task<DateTime> AwaitDelayForFlip(FlipInstance flipInstance);
    bool IsLikelyBot(FlipInstance flipInstance);
    Task<DelayHandler.Summary> Update(IEnumerable<string> ids, DateTime lastCaptchaSolveTime, string licenseOn = null);
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
    private static readonly TimeSpan PreApiDelayExempt = TimeSpan.FromMilliseconds(-1234);
    public static readonly double DelayReduction = 0.3;


    private readonly ITimeProvider timeProvider;
    public TimeSpan CurrentDelay => currentDelay;

    public TimeSpan MacroDelay => macroPenalty;

    private TimeSpan currentDelay = DefaultDelay;
    private TimeSpan macroPenalty = TimeSpan.Zero;
    private double dropoutChance = 0.02;
    private SessionInfo sessionInfo;
    private Random random;
    private SelfUpdatingValue<AccountInfo> accountInfo;
    private IFlipTrackingService flipTrackingService;
    public event Action<TimeSpan> OnDelayChange;

    public DelayHandler(ITimeProvider timeProvider, IFlipTrackingService flipTrackingService, SessionInfo sessionInfo, SelfUpdatingValue<AccountInfo> accountInfo, Random random = null)
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
        var profit = flipInstance.Profit;
        if (dropoutChance * (profit > 6_000_000 ? 3 : 1) > random.NextDouble())
            await timeProvider.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
        var isPreApi = (flipInstance.Auction.Context?.TryGetValue("pre-api", out var preApi) ?? false) && preApi != "recheck";
        if (IsLikelyBot(flipInstance) && (!isPreApi || flipInstance.Profit > 20_000_000 && random.NextDouble() < 0.3))
            return timeProvider.Now;
        if (profit < 200_000 && flipInstance.Finder == Core.LowPricedAuction.FinderType.FLIPPER)
            return timeProvider.Now;
        var myIndex = FlipIndex;
        Interlocked.Increment(ref FlipIndex);
        TimeSpan delay = GetCorrectDelay(myIndex);
        var part1 = delay / 4;
        var part2 = delay - part1;
        await timeProvider.Delay(part1).ConfigureAwait(false);
        var time = timeProvider.Now;
        await timeProvider.Delay(part2).ConfigureAwait(false);
        if (sessionInfo.SessionTier >= AccountTier.SUPER_PREMIUM)
            return time; // no extra balancing for pre api user
        var apiBed = flipInstance.Auction.Start > timeProvider.Now - TimeSpan.FromSeconds(20) && !(flipInstance.Auction.Context?.ContainsKey("pre-api") ?? true);
        var isHighProfit = profit > 2_500_000 || flipInstance.Finder == Core.LowPricedAuction.FinderType.SNIPER && profit > 1_500_000;
        Activity.Current.Log("Applied fairness ");
        if (sessionInfo.IsMacroBot && profit > 1_000_000)
        {
            await timeProvider.Delay(TimeSpan.FromMilliseconds(Math.Min(profit / 20000, 4_000))).ConfigureAwait(false);
            var sendableIn = flipInstance.Auction.Start - timeProvider.Now + TimeSpan.FromSeconds(18);
            if (sendableIn > TimeSpan.Zero && !apiBed)
                await timeProvider.Delay(sendableIn).ConfigureAwait(false);
            if (isPreApi && Random.Shared.NextDouble() < 0.98)
                await timeProvider.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false); // reserve preapi for nonbots
            Activity.Current.Log("Applied BAF " + sendableIn);
        }
        if (isPreApi && Random.Shared.NextDouble() < 0.98)
        {
            await timeProvider.Delay(TimeSpan.FromSeconds(Math.Min(2, delay.TotalSeconds * 5))).ConfigureAwait(false); // reserve preapi for non-macroers
            Activity.Current.Log($"Applied preapi delay {delay} macro: {macroPenalty}");
        }

        if (isHighProfit && (!apiBed || random.NextDouble() < 0.5))
            await timeProvider.Delay(macroPenalty).ConfigureAwait(false);
        else if (profit <= 0)
            await timeProvider.Delay(macroPenalty / 2).ConfigureAwait(false);
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

        // 20% chance of no delay so lowest ping macro doesn't get a huge advantage
        if (random.NextDouble() < 0.8)
            return false;

        var tag = flipInstance.Auction?.Tag;
        var profitPercent = flipInstance.ProfitPercentage;
        var profitSum = flipInstance.Profit;
        if (profitSum > 25_000_000)
            return true;
        return tag != null && (
                    (tag.Contains("DIVAN") || tag == "FROZEN_SCYTHE" || tag.StartsWith("SORROW_")
                    || tag.StartsWith("NECROMANCER_LORD_") || tag.Contains("ASPECT")
                    || tag.EndsWith("SHADOW_FURY") || tag == "HYPERION")
                        && profitPercent > 100
                    || (tag.Contains("CRIMSON") || tag.StartsWith("POWER_WITHER_")
                        || tag == "BAT_WAND" || tag == "DWARF_TURTLE_SHELMET" || tag == "JUJU_SHORTBOW"
                        || tag.Contains("GEMSTONE") || tag.StartsWith("FINAL_DESTINATION"))
                        && profitPercent > 200)
                    || profitPercent > 900
                    || flipInstance.Volume > 42;
    }

    public async Task<Summary> Update(IEnumerable<string> ids, DateTime lastCaptchaSolveTime, string licenseOn = null)
    {
        var lastDelay = currentDelay;
        currentDelay += TimeSpan.FromMilliseconds(2); // to prevent 0 delay in case of errors
        var filteredIds = ids.Where(i => !string.IsNullOrEmpty(i)).ToArray();
        if (filteredIds.Length == 0)
            return new Summary() { Penalty = TimeSpan.FromSeconds(2.5) };
        var breakdown = await flipTrackingService.GetSpeedComp(filteredIds);
        var summary = new Summary();
        if (filteredIds.Length > 1 && licenseOn != null)
        {
            string[] primaryId = [licenseOn];
            var singleBreakdown = await flipTrackingService.GetSpeedComp(primaryId, 25);
            var rate = (singleBreakdown?.ReceivedCount ?? 1) / 100 - (singleBreakdown.Times?.Count ?? 0);
            var dropOut = random.NextDouble() < 0.04;
            if (breakdown.Penalty > 0.9 && random.NextDouble() < 0.3)
                dropOut = true;
            if (singleBreakdown != null && (singleBreakdown.Buys?.Count > 0 || singleBreakdown?.Penalty > 0.01) && rate <= 2 || dropOut)
            {
                singleBreakdown.BadIds = breakdown.BadIds;
                if (Random.Shared.NextDouble() < 0.90)
                    singleBreakdown.MacroedFlips = breakdown.MacroedFlips;
                breakdown = singleBreakdown;
                summary.SingleAccountDelay = true;
            }
        }
        var hourCount = breakdown?.Times?.Where(t => t.TotalSeconds > 1).GroupBy(t => TimeSpan.Parse(t.Age).Hours).Count() ?? 0;
        var recommendedPenalty = breakdown?.Penalty ?? 2;
        currentDelay = TimeSpan.FromSeconds(recommendedPenalty);


        if (!sessionInfo.VerifiedMc)
            currentDelay += TimeSpan.FromSeconds(3);
        else
            summary.VerifiedMc = true;

        summary.LastPurchase = breakdown.Buys?.Values?.DefaultIfEmpty(default).Max() ?? default;

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
        var macroed = breakdown?.MacroedFlips?.Where(m => m.BuyTime > DateTime.UtcNow - TimeSpan.FromHours(6)).Count() ?? 0;
        if (macroed <= 2)
            macroPenalty = TimeSpan.Zero;
        else
        {
            macroPenalty = TimeSpan.FromSeconds(Math.Min(0.13 * macroed, 0.9));
            if (breakdown?.MacroedFlips != null && breakdown.MacroedFlips.Max(f => f.BuyTime) > DateTime.UtcNow - TimeSpan.FromSeconds(180))
                summary.MacroWarning = true;
        }

        // "skip" section
        var nonpurchaseRate = (breakdown?.ReceivedCount ?? 1) / 100 - (breakdown.Times?.Count ?? 0);
        if (nonpurchaseRate > 0)
        {
            Activity.Current?.AddTag("purchaseRate", "1").Log("rate: " + nonpurchaseRate);
            dropoutChance = nonpurchaseRate * 0.04;
            if (currentDelay < TimeSpan.Zero)
                currentDelay = TimeSpan.Zero;
            if (random.NextDouble() < dropoutChance)
            {
                currentDelay += TimeSpan.FromSeconds(0.0001);
                dropoutChance *= 5;
            }
            summary.nonpurchaseRate = nonpurchaseRate;
        }

        summary.HasBadPlayer = (breakdown.BadIds?.Count ?? 0) != 0;
        if (summary.HasBadPlayer && Random.Shared.NextDouble() < 0.9)
        {
            currentDelay -= TimeSpan.FromSeconds(7.2);
            macroPenalty += TimeSpan.FromSeconds(5);
        }
        if (accountInfo.Value?.ShadinessLevel > 50 && macroPenalty < TimeSpan.FromSeconds(2) && Random.Shared.NextDouble() < 0.8)
        {
            // shady accounts keep base delay
            macroPenalty += TimeSpan.FromSeconds(4);
        }
        var lastOne = breakdown?.Times?.LastOrDefault();
        if (lastOne != null)
            summary.ReduceBadActions = TimeSpan.Parse(lastOne.Age) < TimeSpan.FromMinutes(2) && lastOne.TotalSeconds > 3.1 && breakdown.Penalty > 0.1 && breakdown.Buys.Count > 3;

        if (ids.Any(DiHandler.GetService<DelayService>().IsSlowedDown))
            currentDelay += TimeSpan.FromSeconds(4);

        if (accountInfo?.Value?.Tricks?.PenalizeUntil > timeProvider.Now)
        {
            currentDelay *= 1.8;
        }
        if (breakdown.BoughtWorth > 1_000_000_000)
        {
            currentDelay += TimeSpan.FromMilliseconds(20);
        }
        else if (currentDelay < TimeSpan.FromMilliseconds(20) && currentDelay > TimeSpan.Zero)
        {
            Console.WriteLine("Delay too low, setting to 20ms for " + string.Join(", ", ids));
            currentDelay = TimeSpan.Zero;
        }

        if (sessionInfo.SessionTier >= AccountTier.SUPER_PREMIUM)
        {
            currentDelay *= (1 - DelayReduction);
            if (currentDelay > MaxSuperPremiumDelay)
                currentDelay = MaxSuperPremiumDelay;
            macroPenalty *= (1 - DelayReduction);
            if (summary.LastPurchase < DateTime.Now - TimeSpan.FromMinutes(5))
            {
                currentDelay = PreApiDelayExempt;
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
        public int nonpurchaseRate;
        public bool HasBadPlayer;
        public bool ReduceBadActions;
        public DateTime LastPurchase;
        public bool SingleAccountDelay;
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

