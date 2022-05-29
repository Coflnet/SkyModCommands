using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using WebSocketSharp;
using OpenTracing;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Represents a mod session
    /// </summary>
    public class ModSessionLifesycle : IDisposable
    {
        protected MinecraftSocket socket;
        protected OpenTracing.ITracer tracer => socket.tracer;
        public SessionInfo SessionInfo => socket.SessionInfo;
        public string COFLNET = MinecraftSocket.COFLNET;
        public SelfUpdatingValue<FlipSettings> FlipSettings;
        public SelfUpdatingValue<string> UserId;
        public SelfUpdatingValue<AccountInfo> AccountInfo;
        public SelfUpdatingValue<AccountSettings> AccountSettings;
        public SelfUpdatingValue<PrivacySettings> PrivacySettings;
        public OpenTracing.ISpan ConSpan => socket.ConSpan;
        public System.Threading.Timer PingTimer;

        private ConcurrentDictionary<long, DateTime> SentFlips = new ConcurrentDictionary<long, DateTime>();
        private static Prometheus.Counter sentFlipsCount = Prometheus.Metrics.CreateCounter("sky_mod_sent_flips", "How many flip messages were sent");
        public static FlipSettings DEFAULT_SETTINGS => new FlipSettings()
        {
            MinProfit = 100000,
            MinVolume = 20,
            ModSettings = new ModSettings() { ShortNumbers = true },
            Visibility = new VisibilitySettings() { SellerOpenButton = true, ExtraInfoMax = 3, Lore = true }
        };

        public ModSessionLifesycle(MinecraftSocket socket)
        {
            this.socket = socket;
        }

        public async Task<bool> SendFlip(LowPricedAuction flip)
        {
            var Settings = FlipSettings.Value;
            if (Settings.DisableFlips)
                return true;
            var verbose = flip.AdditionalProps.ContainsKey("long wait");
            if (verbose)
                ConSpan.Log("Start sending " + DateTime.Now);
            // pre check already sent flips
            if (SentFlips.ContainsKey(flip.UId))
                return true; // don't double send

            if (flip.AdditionalProps?.ContainsKey("sold") ?? false)
                return BlockedFlip(flip, "sold");
            if (Settings.IsFinderBlocked(flip.Finder))
                return BlockedFlip(flip, "finder " + flip.Finder);

            var flipInstance = FlipperService.LowPriceToFlip(flip);
            // fast match before fill
            Settings.GetPrice(flipInstance, out _, out long profit);
            if (!Settings.BasedOnLBin && Settings.MinProfit > profit)
                return BlockedFlip(flip, "MinProfit");
            var isMatch = (false, "");

            if (verbose)
                ConSpan.Log("before visibility " + DateTime.Now);
            if (!Settings.FastMode)
                try
                {
                    await FlipperService.FillVisibilityProbs(flipInstance, Settings);
                }
                catch (Exception e)
                {
                    socket.Error(e, "filling visibility");
                }

            if (verbose)
                ConSpan.Log("before matching " + DateTime.Now);
            try
            {
                isMatch = Settings.MatchesSettings(flipInstance);
                if (flip.AdditionalProps == null)
                    flip.AdditionalProps = new Dictionary<string, string>();
                flip.AdditionalProps["match"] = isMatch.Item2;
                if (isMatch.Item2.StartsWith("whitelist"))
                    flipInstance.Interesting.Insert(0, "WL");
            }
            catch (Exception e)
            {
                var id = socket.Error(e, "matching flip settings", JSON.Stringify(flip) + "\n" + JSON.Stringify(Settings));
                dev.Logger.Instance.Error(e, "minecraft socket flip settings matching " + id);
                return BlockedFlip(flip, "Error " + e.Message);
            }
            if (Settings != null && !isMatch.Item1)
                return BlockedFlip(flip, isMatch.Item2);

            // this check is down here to avoid filling up the list
            if (!SentFlips.TryAdd(flip.UId, DateTime.Now))
                return true; // make sure flips are not sent twice

            if (verbose)
                ConSpan.Log("building trace " + DateTime.Now);
            using var span = tracer.BuildSpan("Flip").WithTag("uuid", flipInstance.Uuid).AsChildOf(ConSpan.Context).StartActive();
            var settings = Settings;

            var sendTimeTrack = socket.GetService<FlipTrackingService>().ReceiveFlip(flip.Auction.Uuid, SessionInfo.McUuid);
            await Task.Delay(SessionInfo.Penalty);
            await socket.ModAdapter.SendFlip(flipInstance).ConfigureAwait(false);
            if (SessionInfo.LastSpeedUpdate < DateTime.Now - TimeSpan.FromSeconds(50))
            {
                var adjustment = MinecraftSocket.NextFlipTime - DateTime.UtcNow - TimeSpan.FromSeconds(60);
                if (Math.Abs(adjustment.TotalSeconds) < 1)
                    SessionInfo.RelativeSpeed = adjustment;
                SessionInfo.LastSpeedUpdate = DateTime.Now;
            }

            if (verbose)
                ConSpan.Log("sent flip " + DateTime.Now);
            var timeToSend = DateTime.Now - flipInstance.Auction.FindTime;
            flip.AdditionalProps["csend"] = (timeToSend).ToString();

            span.Span.Log("sent");
            socket.LastSent.Enqueue(flip);
            sentFlipsCount.Inc();

            PingTimer.Change(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(55));

            _ = Task.Run(TrackFlipAndCleanup(flip, span, sendTimeTrack, timeToSend)).ConfigureAwait(false);

            if (verbose)
                ConSpan?.Log("exiting " + DateTime.Now);
            return true;
        }

        private Func<Task> TrackFlipAndCleanup(LowPricedAuction flip, IScope span, Task sendTimeTrack, TimeSpan timeToSend)
        {
            return async () =>
            {
                await sendTimeTrack;
                if (timeToSend > TimeSpan.FromSeconds(15) && AccountInfo.Value?.Tier >= AccountTier.PREMIUM && flip.Finder != LowPricedAuction.FinderType.FLIPPER)
                {
                    // very bad, this flip was very slow, create a report
                    using var slowSpan = tracer.BuildSpan("slowFlip").AsChildOf(span.Span).WithTag("error", true).StartActive();
                    slowSpan.Span.Log(JsonConvert.SerializeObject(flip.Auction.Context));
                    slowSpan.Span.Log(JsonConvert.SerializeObject(flip.AdditionalProps));
                    ReportCommand.TryAddingAllSettings(slowSpan);
                }
                // remove dupplicates
                if (SentFlips.Count > 300)
                {
                    foreach (var item in SentFlips.Where(i => i.Value < DateTime.Now - TimeSpan.FromMinutes(2)).ToList())
                    {
                        SentFlips.TryRemove(item.Key, out DateTime value);
                    }
                }
                if (socket.LastSent.Count > 30)
                    socket.LastSent.TryDequeue(out _);
            };
        }

        private bool BlockedFlip(LowPricedAuction flip, string reason)
        {
            socket.TopBlocked.Enqueue(new MinecraftSocket.BlockedElement()
            {
                Flip = flip,
                Reason = reason
            });
            return true;
        }

        public async Task SetupConnectionSettings(string stringId)
        {
            using var loadSpan = socket.tracer.BuildSpan("load").AsChildOf(ConSpan).StartActive();
            SessionInfo.SessionId = stringId;

            PingTimer = new System.Threading.Timer((e) =>
            {
                SendPing();
            }, null, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(50));
            /*SettingsChange cachedSettings = null;
            for (int i = 0; i < 3; i++)
            {
                cachedSettings = await CacheService.Instance.GetFromRedis<SettingsChange>(this.Id.ToString());
                if (cachedSettings != null)
                    break;
                await Task.Delay(800); // backoff to give redis time to recover
            }*/
            UserId = await SelfUpdatingValue<string>.Create("mod", stringId);
            if (UserId.Value == default)
            {
                UserId.OnChange += (newset) => Task.Run(async () => await SubToSettings(newset));
                FlipSettings = await SelfUpdatingValue<FlipSettings>.Create("mod", "flipSettings", () => DEFAULT_SETTINGS);
                Console.WriteLine("waiting for load");
            }
            else
                await SubToSettings(UserId);

            loadSpan.Span.Finish();
            UpdateExtraDelay();
            await SendLoginPromptMessage(stringId);
        }

        private async Task SendLoginPromptMessage(string stringId)
        {
            var index = 1;
            while (UserId.Value == null)
            {
                SendMessage(COFLNET + $"Please {McColorCodes.WHITE}§lclick this [LINK] to login {McColorCodes.GRAY}and configure your flip filters §8(you won't receive real time flips until you do)",
                    GetAuthLink(stringId));
                await Task.Delay(TimeSpan.FromSeconds(60 * index++));

                if (UserId.Value != default)
                    return;
                SendMessage("do /cofl stop to stop receiving this (or click this message)", "/cofl stop");
            }
        }

        protected virtual async Task SubToSettings(string val)
        {
            var flipSettingsTask = SelfUpdatingValue<FlipSettings>.Create(val, "flipSettings", () => DEFAULT_SETTINGS);
            var accountSettingsTask = SelfUpdatingValue<AccountSettings>.Create(val, "accuntSettings");
            AccountInfo = await SelfUpdatingValue<AccountInfo>.Create(val, "accountInfo");
            FlipSettings = await flipSettingsTask;

            // make sure there is only one connection
            AccountInfo.Value.ActiveConnectionId = SessionInfo.ConnectionId;
            _ = AccountInfo.Update(AccountInfo.Value);

            FlipSettings.OnChange += UpdateSettings;
            AccountInfo.OnChange += (ai) => Task.Run(async () => await UpdateAccountInfo(ai));
            if (AccountInfo.Value != default)
                await UpdateAccountInfo(AccountInfo);
            else
                Console.WriteLine("accountinfo is default");

            AccountSettings = await accountSettingsTask;
            SessionInfo.EventBrokerSub = socket.GetService<EventBrokerClient>().SubEvents(val, onchange =>
            {
                Console.WriteLine("received update from event");
                SendMessage(COFLNET + onchange.Message);
            });
            await ApplyFlipSettings(FlipSettings.Value, ConSpan);
        }

        /// <summary>
        /// Makes sure given settings are applied
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="span"></param>
        private async Task ApplyFlipSettings(FlipSettings settings, OpenTracing.ISpan span)
        {
            if (settings == null)
                return;
            try
            {
                if (FlipSettings.Value?.ModSettings?.Chat ?? false)
                    await ChatCommand.MakeSureChatIsConnected(socket);
                if (settings.BasedOnLBin && settings.AllowedFinders != LowPricedAuction.FinderType.SNIPER)
                {
                    socket.SendMessage(new DialogBuilder().Msg(McColorCodes.RED + "Your profit is based on lbin, therefore you should only use the `sniper` flip finder to maximise speed"));
                }
                span.Log(JSON.Stringify(settings));
            }
            catch (Exception e)
            {
                socket.Error(e, "applying flip settings");
            }
        }

        /// <summary>
        /// Called when setting were updated to apply them
        /// </summary>
        /// <param name="settings"></param>
        protected virtual void UpdateSettings(FlipSettings settings)
        {
            using var span = tracer.BuildSpan("SettingsUpdate").AsChildOf(ConSpan.Context)
                    .StartActive();
            var changed = settings.LastChanged;
            if (changed == null)
            {
                changed = socket.FindWhatsNew(FlipSettings.Value, settings);
                if (changed == null)
                    changed = "Settings changed";
            }
            if (changed != "preventUpdateMsg")
                SendMessage($"{COFLNET}{changed}");

            ApplyFlipSettings(settings, span.Span).Wait();
        }

        protected virtual async Task UpdateAccountInfo(AccountInfo info)
        {
            using var span = tracer.BuildSpan("AuthUpdate").AsChildOf(ConSpan.Context)
                    .WithTag("premium", info.Tier.ToString())
                    .WithTag("userId", info.UserId.ToString())
                    .StartActive();

            var userApi = socket.GetService<PremiumService>();
            var expiresTask = userApi.ExpiresWhen(info.UserId);

            try
            {
                if (info.ActiveConnectionId != SessionInfo.ConnectionId && !string.IsNullOrEmpty(info.ActiveConnectionId))
                {
                    // another connection of this account was opened, close this one
                    SendMessage("\n\n" + COFLNET + McColorCodes.GREEN + "We closed this connection because you opened another one", null,
                        "To protect against your mod opening\nmultiple connections which you can't stop,\nwe closed this one.\nThe latest one you opened should still be active");
                    socket.ExecuteCommand("/cofl stop");
                    socket.Close();
                    return;
                }

                if (info.ConIds.Contains("logout"))
                {
                    SendMessage("You have been logged out");
                    socket.Close();
                    return;
                }


                var expires = await expiresTask;
                Console.WriteLine(info.ExpiresAt);
                if (info.ExpiresAt > DateTime.Now && info.Tier == AccountTier.NONE)
                {
                    info.Tier = AccountTier.PREMIUM;
                    info.ExpiresAt = expires;
                }

                UpdateConnectionTier(info, socket.ConSpan);
                if (SessionInfo.SentWelcome)
                    return; // don't send hello again
                SessionInfo.SentWelcome = true;
                var helloTask = SendAuthorizedHello(info);
                SendMessage(socket.formatProvider.WelcomeMessage());
                await Task.Delay(500);
                await MakeSureUserIsVerified(info);
                await helloTask;
                //SendMessage(COFLNET + $"{McColorCodes.DARK_GREEN} click this to relink your account",
                //GetAuthLink(stringId), "You don't need to relink your account. \nThis is only here to allow you to link your mod to the website again should you notice your settings aren't updated");
                return;
            }
            catch (Exception e)
            {
                socket.Error(e, "loading modsocket");
                SendMessage(COFLNET + $"Your settings could not be loaded, please relink again :)");
            }
        }

        private async Task MakeSureUserIsVerified(AccountInfo info)
        {
            var isVerified = await CheckVerificationStatus(info);
            if (!isVerified && info.Tier > 0)
            {
                SendMessage($"{COFLNET} You have premium but you haven't verified your account yet.");
                await Task.Delay(1000);
                SendMessage($"{COFLNET} You have verify your account before you receive flips at max speed.", null, "This is part of our anti macro system and required to make sure you are not connecting from a cracked account");
            }
        }

        public async Task<IEnumerable<string>> GetMinecraftAccountUuids()
        {
            var result = await McAccountService.Instance.GetAllAccounts(UserId.Value);
            if (result == null || result.Count() == 0)
                return new string[] { SessionInfo.McUuid };
            if (!result.Contains(SessionInfo.McUuid))
                result = result.Append(SessionInfo.McUuid);
            return result;
        }

        protected virtual void SendMessage(string message, string click = null, string hover = null)
        {
            socket.SendMessage(message, click, hover);
        }
        protected virtual void SendMessage(ChatPart[] parts)
        {
            socket.SendMessage(parts);
        }
        public virtual string GetAuthLink(string stringId)
        {
            return $"https://sky.coflnet.com/authmod?mcid={SessionInfo.McName}&conId={HttpUtility.UrlEncode(stringId)}";
        }



        public virtual async Task<bool> CheckVerificationStatus(AccountInfo settings)
        {
            var mcUuid = SessionInfo.McUuid;
            var userId = settings.UserId.ToString();
            if (settings.McIds.Contains(SessionInfo.McUuid))
            {
                SessionInfo.VerifiedMc = true;
                return SessionInfo.VerifiedMc;
            }
            var connect = await McAccountService.Instance.ConnectAccount(userId, mcUuid);
            if (connect == null)
            {
                socket.Log("could not get connect result");
                return false;
            }
            if (connect.IsConnected)
            {
                SessionInfo.VerifiedMc = true;
                if (!settings.McIds.Contains(mcUuid))
                    settings.McIds.Add(mcUuid);
                return SessionInfo.VerifiedMc;
            }
            using var verification = tracer.BuildSpan("Verification").AsChildOf(ConSpan.Context).StartActive();
            var activeAuction = await ItemPrices.Instance.GetActiveAuctions(new ActiveItemSearchQuery()
            {
                name = "STICK",
            });
            var bid = connect.Code;
            var r = new Random();

            var targetAuction = activeAuction.Where(a => a.Price < bid).OrderBy(x => r.Next()).FirstOrDefault();
            verification.Span.SetTag("code", bid);
            verification.Span.Log(JSON.Stringify(activeAuction));
            verification.Span.Log(JSON.Stringify(targetAuction));

            socket.SendMessage(new ChatPart(
                $"{COFLNET}You connected from an unkown account. Please verify that you are indeed {SessionInfo.McName} by bidding {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on a random auction. {McColorCodes.YELLOW}[CLICK TO OPEN AN AUCTION FOR A STICK]",
                $"/viewauction {targetAuction?.Uuid}",
                $"{McColorCodes.GRAY}Click to open an auction to bid {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on\nyou can also bid another number with the same digits at the end\neg. 1,234,{McColorCodes.AQUA}{bid}"));

            return false;
        }

        public void UpdateConnectionTier(AccountInfo accountInfo, OpenTracing.ISpan span)
        {
            this.ConSpan.SetTag("tier", accountInfo?.Tier.ToString());
            span.Log("set connection tier to " + accountInfo?.Tier.ToString());
            if (accountInfo == null)
                return;

            if (FlipSettings.Value.DisableFlips)
            {
                SendMessage(COFLNET + "you currently don't receive flips because you disabled them", "/cofl set disableflips false", "click to enable");
                return;
            }

            if (accountInfo.Tier == AccountTier.NONE)
            {
                FlipperService.Instance.AddNonConnection(socket, false);
            }
            if ((accountInfo.Tier.HasFlag(AccountTier.PREMIUM) || accountInfo.Tier.HasFlag(AccountTier.STARTER_PREMIUM)) && accountInfo.ExpiresAt > DateTime.Now)
            {
                FlipperService.Instance.AddConnection(socket, false);
            }
            else if (accountInfo.Tier == AccountTier.PREMIUM_PLUS)
                FlipperService.Instance.AddConnectionPlus(socket, false);
        }


        protected virtual async Task SendAuthorizedHello(AccountInfo accountInfo)
        {
            var user = UserService.Instance.GetUserById(accountInfo.UserId);
            var email = user.Email;
            string anonymisedEmail = UserService.Instance.AnonymiseEmail(email);
            if (this.SessionInfo.McName == null)
                await Task.Delay(800); // allow another half second for the playername to be loaded
            var messageStart = $"Hello {this.SessionInfo.McName} ({anonymisedEmail}) \n";
            if (accountInfo.Tier != AccountTier.NONE && accountInfo.ExpiresAt > DateTime.Now)
                SendMessage(COFLNET + messageStart + $"You have {accountInfo.Tier.ToString()} until {accountInfo.ExpiresAt.ToString("yyyy-MMM-dd hh:mm")} UTC");
            else
                SendMessage(COFLNET + messageStart + $"You use the free version of the flip finder");

            await Task.Delay(300);
        }


        private void SendPing()
        {
            var blockedFlipFilterCount = socket.TopBlocked.Count;
            using var span = tracer.BuildSpan("ping").AsChildOf(ConSpan.Context).WithTag("count", blockedFlipFilterCount).StartActive();
            try
            {
                UpdateExtraDelay();
                if (blockedFlipFilterCount > 0 && SessionInfo.LastBlockedMsg.AddMinutes(FlipSettings.Value.ModSettings.MinutesBetweenBlocked) < DateTime.Now)
                {
                    socket.SendMessage(new ChatPart(COFLNET + $"there were {blockedFlipFilterCount} flips blocked by your filter the last minute",
                        "/cofl blocked",
                        $"{McColorCodes.GRAY} execute {McColorCodes.AQUA}/cofl blocked{McColorCodes.GRAY} to list blocked flips"),
                        new ChatPart(" ", "/cofl void", null));
                    SessionInfo.LastBlockedMsg = DateTime.Now;

                    // remove blocked if clear should fail
                    while (socket.TopBlocked.Count > 345)
                    {
                        socket.TopBlocked.TryDequeue(out _);
                    }
                }
                else
                {
                    socket.Send(Response.Create("ping", 0));

                    UpdateConnectionTier(AccountInfo, span.Span);
                }
                if (blockedFlipFilterCount > 1000)
                    span.Span.SetTag("error", true);
            }
            catch (System.InvalidOperationException)
            {
                socket.RemoveMySelf();
            }
            catch (Exception e)
            {
                span.Span.Log("could not send ping");
                socket.Error(e, "on ping"); // CloseBecauseError(e);
            }
        }

        public void StartTimer(double seconds = 10, string prefix = "§c")
        {
            var mod = this.FlipSettings.Value?.ModSettings;
            if (socket.Version == "1.3-Alpha")
                socket.SendMessage(COFLNET + "You have to update your mod to support the timer");
            else
                socket.Send(Response.Create("countdown", new
                {
                    seconds = seconds,
                    widthPercent = (mod?.TimerX ?? 0) == 0 ? 10 : mod.TimerX,
                    heightPercent = (mod?.TimerY ?? 0) == 0 ? 10 : mod.TimerY,
                    scale = (mod?.TimerScale ?? 0) == 0 ? 2 : mod.TimerScale,
                    prefix = mod?.TimerPrefix ?? prefix,
                    maxPrecision = (mod?.TimerPrecision ?? 0) == 0 ? 3 : mod.TimerPrecision
                }));
        }

        private void UpdateExtraDelay()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(new Random().Next(1, 3000));
                try
                {
                    var penalty = await socket.GetService<FlipTrackingService>()
                            .GetRecommendedPenalty(await GetMinecraftAccountUuids());
                    IScope span = null;
                    if (penalty.Item1 > TimeSpan.Zero)
                    {
                        SessionInfo.Penalty = penalty.Item1;
                        span = tracer.BuildSpan("nerv").AsChildOf(ConSpan).StartActive();
                        span.Span.SetTag("time", penalty.ToString());
                    }
                    else
                        SessionInfo.Penalty = TimeSpan.Zero;
                    if (!SessionInfo.VerifiedMc)
                    {
                        SessionInfo.Penalty += TimeSpan.FromSeconds(3);
                        span?.Span.Log("penalty for not verified mc");
                    }
                    if (penalty.Item2 > 3 && GetLastCaptchaSolveTime() < DateTime.UtcNow - TimeSpan.FromHours(1.5))
                    {
                        SendMessage("Hello there, you acted suspiciously like a macro bot (flipped consistently for multiple hours). \nplease select the correct answer to prove that you are not.", null, "You are delayed until you do");
                        SendMessage(new CaptchaGenerator().SetupChallenge(socket, SessionInfo));
                        SessionInfo.Penalty += TimeSpan.FromSeconds(12);
                        span?.Span.Log("failed macro");
                    }

                }
                catch (Exception e)
                {
                    socket.Error(e, "retrieving penalty");
                }
            });
        }

        private DateTime GetLastCaptchaSolveTime()
        {
            return (AccountInfo.Value.LastCaptchaSolve > SessionInfo.LastCaptchaSolve ? AccountInfo.Value.LastCaptchaSolve : SessionInfo.LastCaptchaSolve);
        }

        public void Dispose()
        {
            FlipSettings.Dispose();
            UserId.Dispose();
            AccountInfo?.Dispose();
            SessionInfo?.Dispose();
            PingTimer?.Dispose();
        }
    }
}
