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
                await FlipperService.FillVisibilityProbs(flipInstance, Settings);

            if (verbose)
                ConSpan.Log("before matching " + DateTime.Now);
            try
            {
                isMatch = Settings.MatchesSettings(flipInstance);
                if (flip.AdditionalProps == null)
                    flip.AdditionalProps = new Dictionary<string, string>();
                flip.AdditionalProps["match"] = isMatch.Item2;
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

            if (verbose)
                ConSpan.Log("sent flip " + DateTime.Now);
            flip.AdditionalProps["csend"] = (DateTime.Now - flipInstance.Auction.FindTime).ToString();

            span.Span.Log("sent");
            socket.LastSent.Enqueue(flip);
            sentFlipsCount.Inc();

            PingTimer.Change(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(55));

            _ = Task.Run(async () =>
            {
                await sendTimeTrack;
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
            }).ConfigureAwait(false);

            if (verbose)
                ConSpan.Log("exiting " + DateTime.Now);
            return true;
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
            Console.WriteLine("conId is : " + stringId);
            UserId = await SelfUpdatingValue<string>.Create("mod", stringId);
            if (UserId.Value == default)
            {
                UserId.OnChange += SubToSettings;
                FlipSettings = await SelfUpdatingValue<FlipSettings>.Create("mod", "flipSettings", () => DEFAULT_SETTINGS);
                Console.WriteLine("waiting for load");
            }
            else
                SubToSettings(UserId);

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

        protected virtual void SubToSettings(string val)
        {
            FlipSettings = SelfUpdatingValue<FlipSettings>.Create(val, "flipSettings", () => DEFAULT_SETTINGS).Result;
            AccountInfo = SelfUpdatingValue<AccountInfo>.Create(val, "accountInfo").Result;

            // make sure there is only one connection
            AccountInfo.Value.ActiveConnectionId = SessionInfo.ConnectionId;
            _ = AccountInfo.Update(AccountInfo.Value);

            FlipSettings.OnChange += UpdateSettings;
            AccountInfo.OnChange += (ai) => Task.Run(async () => await UpdateAccountInfo(ai));
            if (AccountInfo.Value != default)
                Task.Run(async () => await UpdateAccountInfo(AccountInfo));
            else
                Console.WriteLine("accountinfo is default");

            _ = ApplyFlipSettings(FlipSettings.Value, ConSpan);
            AccountSettings = SelfUpdatingValue<AccountSettings>.Create(val, "accuntSettings").Result;
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
            var changed = socket.FindWhatsNew(FlipSettings.Value, settings);
            if (string.IsNullOrWhiteSpace(changed))
                changed = "Settings changed";
            SendMessage($"{COFLNET} {changed}");

            ApplyFlipSettings(settings, span.Span).Wait();
        }

        protected virtual async Task UpdateAccountInfo(AccountInfo info)
        {
            using var span = tracer.BuildSpan("AuthUpdate").AsChildOf(ConSpan.Context)
                    .WithTag("premium", info.Tier.ToString())
                    .WithTag("userId", info.UserId.ToString())
                    .StartActive();
            if (info == null)
                return;
            try
            {
                if (info.ActiveConnectionId != SessionInfo.ConnectionId && !string.IsNullOrEmpty(info.ActiveConnectionId))
                {
                    // another connection of this account was opened, close this one
                    SendMessage("\n\n" +COFLNET + McColorCodes.GREEN + "We closed this connection because you opened another one", null, 
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
                //MigrateSettings(cachedSettings);
                /*ApplySetting(cachedSettings);*/
                UpdateConnectionTier(info, socket.ConSpan);
                if (SessionInfo.SentWelcome)
                    return; // don't send hello again
                SessionInfo.SentWelcome = true;
                var helloTask = SendAuthorizedHello(info);
                SendMessage(socket.formatProvider.WelcomeMessage());
                await Task.Delay(500);
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

        protected virtual void SendMessage(string message, string click = null, string hover = null)
        {
            socket.SendMessage(message, click, hover);
        }
        protected virtual void SendMessage(ChatPart[] parts)
        {
            socket.SendMessage(parts);
        }
        protected virtual string GetAuthLink(string stringId)
        {
            return $"https://sky.coflnet.com/authmod?mcid={SessionInfo.McName}&conId={HttpUtility.UrlEncode(stringId)}";
        }


        protected virtual async Task<OpenTracing.IScope> ModGotAuthorised(AccountInfo settings)
        {
            var span = tracer.BuildSpan("Authorized").AsChildOf(ConSpan.Context).StartActive();
            try
            {
                await SendAuthorizedHello(settings);
                SendMessage($"Authorized connection you can now control settings via the website");
                await Task.Delay(TimeSpan.FromSeconds(20));
                SendMessage($"Remember: the format of the flips is: §dITEM NAME §fCOST -> MEDIAN");
            }
            catch (Exception e)
            {
                socket.Error(e, "settings authorization");
                span.Span.Log(e.Message);
            }
            try
            {
                await CheckVerificationStatus(settings);
            }
            catch (Exception e)
            {
                socket.Error(e, "verification failed");
            }

            return span;
        }

        protected virtual async Task CheckVerificationStatus(AccountInfo settings)
        {
            var connect = await McAccountService.Instance.ConnectAccount(settings.UserId.ToString(), SessionInfo.McUuid);
            if (connect.IsConnected)
                return;
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
                $"{COFLNET}You connected from an unkown account. Please verify that you are indeed {SessionInfo.McName} by bidding {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on a random auction.",
                $"/viewauction {targetAuction?.Uuid}",
                $"{McColorCodes.GRAY}Click to open an auction to bid {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on\nyou can also bid another number with the same digits at the end\neg. 1,234,{McColorCodes.AQUA}{bid}"));

        }

        public void UpdateConnectionTier(AccountInfo accountInfo, OpenTracing.ISpan span)
        {
            this.ConSpan.SetTag("tier", accountInfo?.Tier.ToString());
            span.Log("set connection tier to " + accountInfo?.Tier.ToString());
            if (accountInfo == null)
                return;
            if (DateTime.Now < new DateTime(2022, 1, 22))
            {
                FlipperService.Instance.AddConnection(socket, false);
            }
            else if (accountInfo.Tier == AccountTier.NONE)
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
                SendMessage(COFLNET + messageStart + $"You have {accountInfo.Tier.ToString()} until {accountInfo.ExpiresAt}");
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
                if (blockedFlipFilterCount > 0)
                {
                    socket.SendMessage(new ChatPart(COFLNET + $"there were {blockedFlipFilterCount} flips blocked by your filter the last minute",
                        "/cofl blocked",
                        $"{McColorCodes.GRAY} execute {McColorCodes.AQUA}/cofl blocked{McColorCodes.GRAY} to list blocked flips"),
                        new ChatPart(" ", "/cofl void", null));
                }
                else
                {
                    socket.Send(Response.Create("ping", 0));

                    UpdateConnectionTier(AccountInfo, span.Span);
                }
                if (blockedFlipFilterCount > 1000)
                    span.Span.SetTag("error", true);
                UpdateExtraDelay();
            }
            catch (Exception e)
            {
                span.Span.Log("could not send ping");
                socket.Error(e, "on ping"); // CloseBecauseError(e);
            }
        }

        public void StartTimer(double seconds = 10, string prefix = "§c")
        {
            if (socket.Version == "1.3-Alpha")
                socket.SendMessage(COFLNET + "You have to update your mod to support the timer");
            else
                socket.Send(Response.Create("countdown", new { seconds = seconds, widthPercent = 10, heightPercent = 10, scale = 2, prefix = prefix, maxPrecision = 3 }));
        }

        private void UpdateExtraDelay()
        {
            _ = Task.Run(async () =>
            {
                try
                {

                    var penalty = await socket.GetService<FlipTrackingService>().GetRecommendedPenalty(SessionInfo.McUuid);
                    if (penalty > TimeSpan.Zero)
                    {
                        SessionInfo.Penalty = penalty;
                        using var span = tracer.BuildSpan("nerv").AsChildOf(ConSpan).StartActive();
                        span.Span.SetTag("time", penalty.ToString());
                    }
                    else
                        SessionInfo.Penalty = TimeSpan.Zero;
                }
                catch (Exception e)
                {
                    socket.Error(e, "retrieving penalty");
                }
            });
        }

        public void Dispose()
        {
            FlipSettings.Dispose();
            UserId.Dispose();
            AccountInfo?.Dispose();
            PingTimer?.Dispose();
        }
    }
}
