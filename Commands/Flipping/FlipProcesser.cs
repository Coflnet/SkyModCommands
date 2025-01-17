using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.ModCommands.Tutorials;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class FlipProcesser
    {
        private static Prometheus.Counter sentFlipsCount = Prometheus.Metrics.CreateCounter("sky_mod_sent_flips", "How many flip messages were sent");
        private static Prometheus.Counter sentFlipProfit = Prometheus.Metrics.CreateCounter("sky_mod_flip_profit", "Adds up the estimated profit of flips sent");
        private static Prometheus.Counter sentFlipValue = Prometheus.Metrics.CreateCounter("sky_mod_flip_value", "Adds up the estimated value of flips sent");
        private static Prometheus.Histogram flipSendTiming = Prometheus.Metrics.CreateHistogram("sky_mod_send_time", "Full run through time of flips");
        private static Prometheus.Histogram processingTiming = Prometheus.Metrics.CreateHistogram("sky_mod_process_time", "Processing time of flips");
        private static Prometheus.Counter preApiFlipSent = Prometheus.Metrics.CreateCounter("sky_mod_flips_sent_preapi", "Flips sent to a preapi user");

        private ConcurrentDictionary<long, DateTime> SentFlips = new ConcurrentDictionary<long, DateTime>();
        protected MinecraftSocket socket;
        private FlipSettings Settings => socket.Settings;
        private ISpamController spamController;
        private IDelayHandler delayHandler;
        private IDelayExemptList delayExemptList;
        private int waitingBedFlips = 0;
        private int _blockedFlipCounter = 0;
        public int BlockedFlipCount => _blockedFlipCounter;

        public FlipProcesser(MinecraftSocket socket, ISpamController spamController, IDelayHandler delayHandler)
        {
            this.socket = socket;
            this.spamController = spamController;
            this.delayHandler = delayHandler;
            delayExemptList = socket.GetService<IDelayExemptList>();
        }

        public async Task NewFlips(IEnumerable<LowPricedAuction> flips)
        {
            if (Settings == null || socket.HasFlippingDisabled())
                return;
            var start = DateTime.UtcNow;
            var maxCostFromPurse = socket.SessionInfo.Purse * (Settings.ModSettings.MaxPercentOfPurse == 0 ? 100 : Settings.ModSettings.MaxPercentOfPurse) / 100;
            var prefiltered = flips.Where(f => !SentFlips.ContainsKey(f.UId)
                && FinderEnabled(f)
                && NotSold(f)
                && CheckHighCompetition(f)
                && (f.Auction.StartingBid < maxCostFromPurse || socket.SessionInfo.Purse == 0 || f.Finder == LowPricedAuction.FinderType.USER))
                .Select(f => (f, instance: FlipperService.LowPriceToFlip(f)))
                .ToList();

            if (Settings != null && !Settings.FastMode && (Settings.BasedOnLBin || (Settings.Visibility?.LowestBin ?? false) || (Settings.Visibility?.Seller ?? false)))
            {
                await LoadAdditionalInfo(prefiltered).ConfigureAwait(false);
            }

            var matches = prefiltered.Where(f =>
                    FlipMatchesSetting(f.f, f.instance)
                    && IsNoDupplicate(f.f)).ToList();
            if (matches.Count == 0)
                return;

            var toSend = matches.Where(f => NotBlockedForSpam(f.instance, f.f)).ToList();
            foreach (var item in toSend)
            {
                var timeToSend = DateTime.UtcNow - item.f.Auction.FindTime;
                item.f.AdditionalProps["dl"] = (timeToSend).ToString();
                item.f.AdditionalProps["ft"] = (DateTime.UtcNow - start).ToString();
            }
            using (var span = socket.CreateActivity("Flip", socket.ConSpan)
                ?.AddTag("uuid", matches.First().f.Auction.Uuid)
                .AddTag("batchSize", matches.Count))
                try
                {
                    span.Log($"Before send {DateTime.UtcNow.TimeOfDay:hh\\:mm\\:ss\\.fff}");
                    await SendAfterDelay(toSend.ToList()).ConfigureAwait(false);
                    span.Log($"After send {DateTime.UtcNow.TimeOfDay:hh\\:mm\\:ss\\.fff}");
                    var sendList = toSend.ToList();
                    span.Log(JsonConvert.SerializeObject(sendList?.Select(a => a.f == null ? null : new Dictionary<string, string>(a.f?.AdditionalProps))));
                }
                catch (Exception e)
                {
                    socket.Error(e, "sending flip");
                }

            while (SentFlips.Count > 700)
            {
                foreach (var item in SentFlips.Where(i => i.Value < DateTime.UtcNow - TimeSpan.FromMinutes(2)).ToList())
                {
                    SentFlips.TryRemove(item.Key, out DateTime value);
                }
            }
            while (socket.LastSent.Count > 30)
                socket.LastSent.TryDequeue(out _);
        }

        private bool CheckHighCompetition(LowPricedAuction f)
        {
            if (!Settings.BlockHighCompetitionFlips)
                return true;
            var profit = f.TargetPrice - f.Auction.StartingBid;
            if (!delayExemptList.IsExempt(f) && profit < 8_000_000 && (f.DailyVolume < 20 || profit < 1_500_000))
                return true;
            return BlockedFlip(f, "high competition");
        }

        private async Task LoadAdditionalInfo(List<(LowPricedAuction f, FlipInstance instance)> prefiltered)
        {
            foreach (var flipSum in prefiltered)
            {
                var flipInstance = flipSum.instance;
                Settings.GetPrice(flipInstance, out _, out long profit);
                if (!Settings.BasedOnLBin && Settings.MinProfit > profit)
                    continue;
                try
                {
                    await FlipperService.FillVisibilityProbs(flipInstance, Settings).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    socket.Error(e, "filling visibility");
                }
            }
        }

        private bool NotBlockedForSpam(FlipInstance flipInstance, LowPricedAuction f)
        {
            if (Settings.ModSettings.DisableSpamProtection)
                return true;
            if (socket.LastSent.Where(l => l.Auction.Tag == f.Auction.Tag && f.Auction.Start - l.Auction.Start < TimeSpan.FromMinutes(3) && !flipInstance.IsWhitelisted()).Count() >= 3)
                return BlockedFlip(f, "listing spam");
            if (!spamController.ShouldBeSent(flipInstance))
                return BlockedFlip(f, "spam");
            return true;
        }

        private bool IsNoDupplicate(LowPricedAuction flip)
        {
            // this check is down here to avoid filling up the list
            if (SentFlips.TryAdd(flip.UId, DateTime.UtcNow))
                return true; // make sure flips are not sent twice
            return false;
        }

        private bool FlipMatchesSetting(LowPricedAuction flip, FlipInstance flipInstance)
        {
            if (flip == null || Settings == null)
                return false;
            var isMatch = (false, "");
            try
            {
                isMatch = Settings.MatchesSettings(flipInstance);
                if (flip.AdditionalProps == null)
                    flip.AdditionalProps = [];
                flip.AdditionalProps["match"] = isMatch.Item2;
                if (flipInstance.Context == null)
                    flipInstance.Context = [];
                flipInstance.Context["match"] = isMatch.Item2;
            }
            catch (Exception e)
            {
                var id = socket.Error(e, "matching flip settings", JSON.Stringify(flip) + "\n\n" + JSON.Stringify(flip.Auction.Context) + "\n\n" + JSON.Stringify(Settings));
                dev.Logger.Instance.Error(e, "minecraft socket flip settings matching " + id);
                return BlockedFlip(flip, "Error " + e.Message);
            }
            if (Settings != null && !isMatch.Item1)
                return BlockedFlip(flip, isMatch.Item2);
            return true;
        }

        private bool NotSold(LowPricedAuction flip)
        {
            if (flip.AdditionalProps?.ContainsKey("sold") ?? false)
                return BlockedFlip(flip, "sold");
            else
                return true;
        }

        private bool FinderEnabled(LowPricedAuction flip)
        {
            if (Settings?.IsFinderBlocked(flip.Finder) ?? false)
                if (flip.Finder == LowPricedAuction.FinderType.USER)
                    return false;
                else if (flip.TargetPrice > 2_000_000 || Random.Shared.NextDouble() < 0.1)
                    return BlockedFlip(flip, "finder " + flip.Finder);
                else
                    return false;
            else
                return true;
        }

        private async Task SendAfterDelay(IEnumerable<(LowPricedAuction f, FlipInstance instance)> flips)
        {
            var flipsWithTime = flips.Select(f => (f.instance, f.f.Auction.Start + TimeSpan.FromSeconds(20) - DateTime.UtcNow, lp: f.f));
            var bedsToWaitFor = flipsWithTime.Where(f => f.Item2 > TimeSpan.FromSeconds(3.1) && !(Settings?.ModSettings.NoBedDelay ?? false));
            var noBed = flipsWithTime.ExceptBy(bedsToWaitFor.Select(b => b.lp.Auction.Uuid), b => b.lp.Auction.Uuid).Select(f => (f.instance, f.lp));
            var toSendInstant = noBed.Where(f => (Random.Shared.NextDouble() < 0.33 && delayExemptList.IsExempt(f.lp) || delayHandler.IsLikelyBot(f.instance)) && !f.lp.IsPreApi()).ToList();
            if (socket?.SessionInfo?.SessionTier >= AccountTier.PREMIUM_PLUS)
                foreach (var item in flips)
                {
                    var fullTime = DateTime.UtcNow - item.f.Auction.FindTime;
                    if (TimeSpan.TryParse(item.f.Auction.Context?.GetValueOrDefault("fT", "xy"), out var ft))
                    {
                        var estimate = (fullTime - ft).TotalSeconds;
                        if (estimate < 5) // ignore outliers
                            processingTiming.Observe(estimate);
                    }
                    flipSendTiming.Observe(fullTime.TotalSeconds);
                }
            if (toSendInstant.Count > 0)
                Activity.Current.Log("Sending instant flips");
            foreach (var item in toSendInstant)
            {
                await SendAndTrackFlip(item.instance, item.lp, DateTime.UtcNow, true).ConfigureAwait(false);
            }
            var toSendDelayed = noBed.ExceptBy(toSendInstant.Select(b => b.lp.Auction.Uuid), b => b.lp.Auction.Uuid).ToList();
            if (toSendDelayed.Count == 0)
                return;
            await SendDelayed(noBed, toSendDelayed).ConfigureAwait(false);
            Activity.Current.Log("Waiting for beds");
            // beds
            foreach (var item in bedsToWaitFor.OrderBy(b => b.Item2))
            {
                item.lp.AdditionalProps["bed"] = item.Item2.ToString();
                if (socket.sessionLifesycle.CurrentDelay > DelayHandler.MaxSuperPremiumDelay && Random.Shared.NextDouble() < 0.8)
                {
                    await Task.Delay(item.Item2).ConfigureAwait(false);
                    Activity.Current.Log("Bed await");
                    await SendAndTrackFlip(item.instance, item.lp, DateTime.UtcNow).ConfigureAwait(false);
                    continue;
                }
                await WaitForBedToSend(item).ConfigureAwait(false);
            }
        }

        private async Task SendDelayed(IEnumerable<(FlipInstance instance, LowPricedAuction lp)> noBed, IEnumerable<(FlipInstance instance, LowPricedAuction lp)> toSendDelayed)
        {
            var bestFlip = noBed.Select(f => f.instance).MaxBy(f => f.Profit);
            if (bestFlip == null)
                return;
            var beforeWait = DateTime.UtcNow;
            var sendTime = await delayHandler.AwaitDelayForFlip(bestFlip);
            Activity.Current.Log($"Sending flip delayed {sendTime}");
            foreach (var item in toSendDelayed)
            {
                await SendAndTrackFlip(item.instance, item.lp, DateTime.UtcNow, true).ConfigureAwait(false);
                item.lp.AdditionalProps["it"] = beforeWait.ToString();
            }
        }

        private async Task WaitForBedToSend((FlipInstance instance, TimeSpan, LowPricedAuction lp) item)
        {
            Interlocked.Increment(ref waitingBedFlips);
            var flip = item.instance;
            var endsIn = flip.Auction.Start + TimeSpan.FromSeconds(17) - DateTime.UtcNow;
            if (socket.Settings.ModSettings.DisplayTimer)
                socket.sessionLifesycle.StartTimer(endsIn.TotalSeconds, McColorCodes.GREEN + "Bed in: §c");
            socket.SendSound("note.bass");
            if (endsIn > TimeSpan.Zero)
                await Task.Delay(endsIn).ConfigureAwait(false);
            Interlocked.Decrement(ref waitingBedFlips);
            if (waitingBedFlips == 0)
            {
                socket.sessionLifesycle.StartTimer(0, "clear timer");
                socket.ScheduleTimer();
            }
            // update interesting props because the bed time is different now
            flip.Interesting = Helper.PropertiesSelector.GetProperties(flip.Auction)
                            .OrderByDescending(a => a.Rating).Select(a => a.Value).ToList();

            await SendAndTrackFlip(flip, item.lp, flip.Auction.Start + TimeSpan.FromSeconds(19.9)).ConfigureAwait(false);
        }

        private async Task SendAndTrackFlip(FlipInstance item, LowPricedAuction flip, DateTime sendTime, bool blockSold = false)
        {
            var isSold = socket.GetService<IIsSold>().IsSold(flip.Auction.Uuid);
            if (isSold)
            {
                BlockedFlip(flip, "sold");
                if (blockSold && (Settings?.Visibility?.HideSoldAuction ?? false))
                    return;
            }
            Activity.Current?.Log("Initiating send");
            await socket.ModAdapter.SendFlip(item).ConfigureAwait(false);
            Activity.Current?.Log("Sent flip");
            if (flip.AdditionalProps.ContainsKey("isRR") && socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.SUPER_PREMIUM))
                await socket.TriggerTutorial<RoundRobinTutorial>().ConfigureAwait(false);
            if (flip.Finder == LowPricedAuction.FinderType.SNIPER_MEDIAN && flip.Auction.FlatenedNBT.Count >= 3)
                await socket.TriggerTutorial<Flipping>().ConfigureAwait(false);

            var timeToSend = DateTime.UtcNow - item.Auction.FindTime;
            if (isSold)
                return; // no need to track sold flips
            _ = socket.TryAsyncTimes(async () =>
            {
                if (socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.SUPER_PREMIUM))
                    preApiFlipSent.Inc();
                await Task.Delay(500);

                // this is actually syncronous
                await socket.GetService<IFlipReceiveTracker>()
                    .ReceiveFlip(item.Auction.Uuid, socket.sessionLifesycle.SessionInfo.McUuid, sendTime);

                flip.AdditionalProps["csend"] = (timeToSend).ToString();

                socket.LastSent.Enqueue(flip);
                sentFlipsCount.Inc();
                if (item.Profit > 0)
                {
                    sentFlipProfit.Inc(item.Profit);
                    sentFlipValue.Inc(item.Target);
                }
                if (Settings.DebugMode)
                    socket.SendMessage($"Sent flip {flip.Auction.ItemName} {flip.Auction.StartingBid}->{flip.TargetPrice}", $"https://sky.coflnet.com/auction/{flip.Auction.Uuid}", "Open in browser");

                socket.sessionLifesycle.PingTimer?.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(59));

                if (timeToSend > TimeSpan.FromSeconds(15) && socket.sessionLifesycle.TierManager.HasAtLeast(AccountTier.PREMIUM)
                    && flip.Finder != LowPricedAuction.FinderType.FLIPPER && !(item.Interesting.FirstOrDefault()?.StartsWith("Bed") ?? false))
                {
                    // very bad, this flip was very slow, create a report
                    using var slowSpan = socket.CreateActivity("slowFlip", socket.ConSpan);
                    slowSpan.Log(JsonConvert.SerializeObject(flip.Auction.Context));
                    slowSpan.Log(JsonConvert.SerializeObject(flip.AdditionalProps));
                    ReportCommand.TryAddingAllSettings(socket, slowSpan);
                }

                if (flip.TargetPrice - flip.Auction.StartingBid > 6_000_000 && socket.CurrentRegion == Region.EU)
                    socket.GetService<PreApiService>().CheckHighProfitpurchaser(socket, flip);
            }, "tracking flip");
        }

        public bool BlockedFlip(LowPricedAuction flip, string reason)
        {
            if (socket.TopBlocked.Take(100).Any(b => b.Flip.Auction.Uuid == flip.Auction.Uuid && b.Flip.TargetPrice == flip.TargetPrice))
                return false; // don't count block twice
            if (flip.Finder == LowPricedAuction.FinderType.TFM && Random.Shared.Next(0, 100) > 5 && socket.AccountInfo?.UserIdOld > 10)
                return false; // ignore 95% of tfm flips until finished
            socket.TopBlocked.Enqueue(new()
            {
                Flip = flip,
                Reason = reason
            });
            Activity.Current?.SetTag("blocked", reason);
            Interlocked.Increment(ref _blockedFlipCounter);
            return false;
        }

        /// <summary>
        /// Has to be execute once a minute to clean up state 
        /// </summary>
        public void MinuteCleanup()
        {
            spamController.Reset();
        }

        public void PingUpdate()
        {
            _blockedFlipCounter = 0;
        }
    }
}
