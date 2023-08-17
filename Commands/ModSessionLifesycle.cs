using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using WebSocketSharp;
using OpenTracing;
using Coflnet.Sky.ModCommands.Services;
using System.Threading;
using System.Text;
using System.Diagnostics;
using Coflnet.Sky.ModCommands.Tutorials;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Represents a mod session
    /// </summary>
    public class ModSessionLifesycle : IDisposable
    {
        protected MinecraftSocket socket;
        protected ActivitySource tracer => socket.tracer;
        public SessionInfo SessionInfo => socket.SessionInfo;
        public string COFLNET = MinecraftSocket.COFLNET;
        public SelfUpdatingValue<FlipSettings> FlipSettings;
        public SelfUpdatingValue<string> UserId;
        public SelfUpdatingValue<AccountInfo> AccountInfo;
        public SelfUpdatingValue<AccountSettings> AccountSettings;
        public SelfUpdatingValue<PrivacySettings> PrivacySettings;
        public Activity ConSpan => socket.ConSpan;
        public System.Threading.Timer PingTimer;
        private SpamController spamController = new SpamController();
        public IDelayHandler DelayHandler { get; set; }
        public VerificationHandler VerificationHandler;
        public FlipProcesser flipProcesser;
        public TimeSpan CurrentDelay => DelayHandler?.CurrentDelay ?? MC.DelayHandler.DefaultDelay;
        public event Action<TimeSpan> OnDelayChange
        {
            add
            {
                DelayHandler.OnDelayChange += value;
            }
            remove
            {
                DelayHandler.OnDelayChange -= value;
            }
        }

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
            var info = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(socket.AccountInfo);
            SetupFlipProcessor(info);
            VerificationHandler = new VerificationHandler(socket);
        }

        private void SetupFlipProcessor(SelfUpdatingValue<AccountInfo> info)
        {
            DelayHandler = new DelayHandler(TimeProvider.Instance, socket.GetService<FlipTrackingService>(), this.SessionInfo, info);

            flipProcesser = new FlipProcesser(socket, spamController, DelayHandler);
        }

        public async Task SetupConnectionSettings(string stringId)
        {
            /*    socket.Dialog(db => db.Lines("The welcome pig greets you",
    "           __,---.__",
    "        ,-'          `-.__",
    @"      &/           `._\ _\",
    "       /               ' '._",
    @"      |   ,               ("")",
    @"      |__,'`-..--|__|--''"));

                socket.Dialog(db => db.ForEach("ayz🤨🤔🇧🇾:|,:-.#ä+!^°~´` ", (db, c) => db.ForEach("01234567890123456789", (idb, ignore) => idb.Msg(c.ToString())).MsgLine("|")));
                socket.Dialog(db => db.LineBreak().ForEach(":;", (db, c) => db.ForEach("012345678901234567890123456789", (idb, ignore) => idb.Msg(c.ToString())).MsgLine("|")));
                socket.Dialog(db => db.LineBreak().ForEach("´", (db, c) => db.ForEach("012345678901234567890123", (idb, ignore) => idb.Msg(c.ToString())).MsgLine("|")));
    */
            using var loadSpan = socket.CreateActivity("loadSession", ConSpan);
            SessionInfo.SessionId = stringId;

            PingTimer = new System.Threading.Timer((e) =>
            {
                SendPing();
            }, null, TimeSpan.FromSeconds(59), TimeSpan.FromSeconds(59));

            UserId = await SelfUpdatingValue<string>.Create("mod", stringId);
            _ = socket.TryAsyncTimes(() => SendLoginPromptMessage(stringId), "login prompt");
            if (MinecraftSocket.IsDevMode)
            {
                SendMessage(COFLNET + "You are in dev mode, login link would be " + GetAuthLink(stringId));
                await UserId.Update("1");
            }
            if (UserId.Value == default)
            {
                using var waitLogin = socket.CreateActivity("waitLogin", ConSpan);
                UserId.OnChange += (newset) => Task.Run(async () => await SubToSettings(newset));
                FlipSettings = await SelfUpdatingValue<FlipSettings>.CreateNoUpdate(() => DEFAULT_SETTINGS);
                SubSessionToEventsFor(SessionInfo.McUuid);
            }
            else
            {
                using var sub2SettingsSpan = socket.CreateActivity("sub2Settings", ConSpan);
                await SubToSettings(UserId);
            }

            loadSpan.Dispose();
            UpdateExtraDelay();
        }

        private async Task SendLoginPromptMessage(string stringId)
        {
            var index = 1;
            while (UserId.Value == null)
            {
                socket.ModAdapter.SendLoginPrompt(GetAuthLink(stringId));
                await Task.Delay(TimeSpan.FromSeconds(300 * index++)).ConfigureAwait(false);

                if (UserId.Value != default)
                    return;
                SendMessage("do /cofl stop to stop receiving this (or click this message)", "/cofl stop");
            }
        }

        protected virtual async Task SubToSettings(string userId)
        {
            ConSpan.Log("subbing to settings of " + userId);
            var flipSettingsTask = SelfUpdatingValue<FlipSettings>.Create(userId, "flipSettings", () => DEFAULT_SETTINGS);
            var accountSettingsTask = SelfUpdatingValue<AccountSettings>.Create(userId, "accountSettings");
            AccountInfo = await SelfUpdatingValue<AccountInfo>.Create(userId, "accountInfo", () => new AccountInfo() { UserId = userId });
            FlipSettings = await flipSettingsTask;

            // make sure there is only one connection
            AccountInfo.Value.ActiveConnectionId = SessionInfo.ConnectionId;
            _ = socket.TryAsyncTimes(() => AccountInfo.Update(AccountInfo.Value), "accountInfo update");

            FlipSettings.OnChange += UpdateSettings;
            FlipSettings.ShouldPreventUpdate = (fs) => fs?.Changer == SessionInfo.ConnectionId;
            AccountInfo.OnChange += (ai) => Task.Run(async () => await UpdateAccountInfo(ai), new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);
            if (AccountInfo.Value != default)
                await UpdateAccountInfo(AccountInfo);
            else
                Console.WriteLine("accountinfo is default");

            SetupFlipProcessor(AccountInfo);
            AccountSettings = await accountSettingsTask;
            if (userId != null)
                SubSessionToEventsFor(userId);
            await ApplyFlipSettings(FlipSettings.Value, ConSpan);
        }

        private void SubSessionToEventsFor(string val)
        {
            SessionInfo.EventBrokerSub?.Unsubscribe();
            SessionInfo.EventBrokerSub = socket.GetService<EventBrokerClient>().SubEvents(val, onchange =>
            {
                Console.WriteLine("received update from event");
                SendMessage(COFLNET + onchange.Message);
            });
        }

        /// <summary>
        /// Makes sure given settings are applied
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="span"></param>
        private async Task ApplyFlipSettings(FlipSettings settings, Activity span)
        {
            if (settings == null)
                return;
            var testFlip = BlacklistCommand.GetTestFlip("test");
            try
            {
                if (settings.BasedOnLBin && settings.AllowedFinders != LowPricedAuction.FinderType.SNIPER)
                {
                    socket.SendMessage(new DialogBuilder().CoflCommand<SetCommand>(McColorCodes.RED + "Your profit is based on lbin, therefore you should only use the `sniper` flip finder to maximise speed", "finders sniper", "Click to only use the sniper"));
                }
                if (settings.Visibility.LowestBin && settings.AllowedFinders != LowPricedAuction.FinderType.SNIPER && !SessionInfo.LbinWarningSent)
                {
                    SessionInfo.LbinWarningSent = true;
                    socket.SendMessage(new DialogBuilder().CoflCommand<SetCommand>(McColorCodes.RED + "You enabled display of lbin on a flip finder that is not based on lbin (but median). "
                        + "That slows down flips because the lbin has to be searched for before the flip is sent to you."
                        + "If you are okay with that, ignore this warning. If you want flips faster click this to disable displaying the lbin",
                        "showlbin false",
                        $"You can also enable only lbin based flips \nby executing {McColorCodes.AQUA}/cofl set finders sniper.\nClicking this will hide lbin in flip messages. \nYou can still see lbin in item descriptions."));
                }
                settings.CopyListMatchers(this.FlipSettings);
                // preload flip settings
                settings.MatchesSettings(testFlip);
                span.Log(JSON.Stringify(settings));
                socket.GetService<FlipperService>().UpdateFilterSumaries();
                if (FlipSettings.Value?.ModSettings?.Chat ?? false)
                    await ChatCommand.MakeSureChatIsConnected(socket);
            }
            catch (Exception e)
            {
                socket.Error(e, "applying flip settings");
                CheckListValidity(testFlip, settings.BlackList);
                CheckListValidity(testFlip, settings.WhiteList, true);
            }
        }

        private void CheckListValidity(FlipInstance testFlip, List<ListEntry> blacklist, bool whiteList = false)
        {
            foreach (var item in blacklist.ToList())
            {
                try
                {
                    var expression = item.GetExpression();
                    expression.Compile()(testFlip);
                }
                catch (System.Exception e)
                {
                    if (item.filter.Any(f => f.Key.ToLower() == "seller"))
                    {
                        blacklist.Remove(item);
                        socket.Dialog(db => db.Lines($"{McColorCodes.RED}You had a seller filter in your {(whiteList ? "whitelist" : "blacklist")} for a playername that does no longer exist.",
                                $"The following element was automatically removed: {BlacklistCommand.FormatEntry(item)}"));
                        continue;
                    }
                    var formatted = BlacklistCommand.FormatEntry(item);
                    socket.Error(e, "compiling expression " + formatted);
                    WhichBLEntryCommand.SendRemoveMessage(socket, item, McColorCodes.RED + "Please fix or remove this element on your blacklist, it is invalid: " + formatted, whiteList);
                }
            }
        }

        /// <summary>
        /// Called when setting were updated to apply them
        /// </summary>
        /// <param name="settings"></param>
        protected virtual void UpdateSettings(FlipSettings settings)
        {
            using var span = socket.CreateActivity("SettingsUpdate", ConSpan);
            var changed = settings.LastChanged;
            if (changed == null)
            {
                changed = "Settings changed";
            }
            if (changed != "preventUpdateMsg")
                SendMessage($"{COFLNET}{changed}");
            span.AddTag("changed", changed);
            ApplyFlipSettings(settings, span).Wait();
        }

        protected virtual async Task UpdateAccountInfo(AccountInfo info)
        {
            using var span = socket.CreateActivity("AuthUpdate", ConSpan)
                    .AddTag("premium", info.Tier.ToString())
                    .AddTag("userId", info.UserId);

            try
            {
                var userIsVerifiedTask = VerificationHandler.MakeSureUserIsVerified(info);
                if (info.UserId.IsNullOrEmpty())
                {
                    info.UserId = socket.UserId;
                    await AccountInfo.Update(info);
                }
                var userIsTest = info.UserIdOld > 0 && info.UserIdOld < 10;
                if (info.ActiveConnectionId != SessionInfo.ConnectionId && !string.IsNullOrEmpty(info.ActiveConnectionId) && !userIsTest)
                {
                    // wait for settings sync
                    await Task.Delay(5000).ConfigureAwait(false);
                    if (info.ActiveConnectionId != SessionInfo.ConnectionId)
                    {
                        // another connection of this account was opened, close this one
                        SendMessage("\n\n" + COFLNET + McColorCodes.GREEN + "Closing this connection because your account opened another one. There can only be one per account. Use /cofl logout to close all.", "/cofl logout",
                            "To protect against your mod opening\nmultiple connections which you can't stop,\nwe closed this one.\nThe latest one you opened should still be active");
                        socket.ExecuteCommand("/cofl stop");
                        span.Log($"connected from somewhere else {info.ActiveConnectionId} != {SessionInfo.ConnectionId}");
                        socket.Close();
                        return;
                    }
                }

                if (info.ConIds.Contains("logout"))
                {
                    SendMessage("You have been logged out");
                    span.Log("force loggout");
                    info.ConIds.Remove("logout");
                    await this.AccountInfo.Update(info);
                    socket.Close();
                    return;
                }

                await UpdateAccountTier(info);

                span.Log(JsonConvert.SerializeObject(info, Formatting.Indented));
                if (SessionInfo.SentWelcome)
                    return; // don't send hello again
                SessionInfo.SentWelcome = true;
                await SendAuthorizedHello(info);

                await WaitForSettingsLoaded(span);
                if (FlipSettings.Value.ModSettings.AutoStartFlipper)
                {
                    SendMessage(socket.formatProvider.WelcomeMessage());
                    SessionInfo.FlipsEnabled = true;
                    UpdateConnectionTier(info, span);
                    span.AddTag("autoStart", "true");
                    if (info.Tier >= AccountTier.PREMIUM_PLUS && SessionInfo.ConnectionType == null && Random.Shared.NextDouble() < 0.5)
                    {
                        socket.Dialog(db => db.MsgLine(McColorCodes.GRAY + "Do you want to try out our new US flipping instance? Click this message",
                            "/cofl connect ws://sky-us.coflnet.com/modsocket",
                            "click to connect to united states instance\nhas lower ping after ah update for us users"));
                    }
                }
                else if (!FlipSettings.Value.ModSettings.AhDataOnlyMode)
                {
                    socket.Dialog(db => db.Msg("What do you want to do?").Break
                        .CoflCommand<FlipCommand>($"> {McColorCodes.GOLD}AH flip  ", "true", $"{McColorCodes.GOLD}Show me flips!\n{McColorCodes.DARK_GREEN}(and reask on every start)\nexecutes {McColorCodes.AQUA}/cofl flip")
                        .CoflCommand<FlipCommand>(McColorCodes.DARK_GREEN + " always ah flip ", "always", McColorCodes.DARK_GREEN + "don't show this again and always show me flips")
                        .CoflCommand<FlipCommand>(McColorCodes.BLUE + " use the pricing data ", "never", "I don't want to flip")
                        .Break);
                    await socket.TriggerTutorial<Welcome>();
                    span.AddTag("autoStart", "false");
                }
                await userIsVerifiedTask;
                socket.Send(Response.Create("loggedIn", new { uuid = SessionInfo.McUuid, verified = SessionInfo.VerifiedMc }));
                return;
            }
            catch (Exception e)
            {
                socket.Error(e, "loading modsocket");
                span.AddTag("error", true);
                SendMessage(COFLNET + $"Your settings could not be loaded, please relink again :)");
            }
        }

        /// <summary>
        /// there can be a racecondition where the flipsettings are not yet loaded
        /// </summary>
        /// <param name="span"></param>
        /// <returns></returns>
        private async Task WaitForSettingsLoaded(Activity span)
        {
            for (int i = 1; i < 10; i++)
            {
                if (!string.IsNullOrEmpty(FlipSettings.Value.Changer))
                    break;
                await Task.Delay(i * 100);
                span.Log("waiting for flipsettings");
            }
        }

        public async Task<AccountTier> UpdateAccountTier(AccountInfo info)
        {
            var userApi = socket.GetService<PremiumService>();
            var previousTier = info.Tier;
            var expiresTask = userApi.GetCurrentTier(info.UserId);
            var expires = await expiresTask;
            info.Tier = expires.Item1;
            info.ExpiresAt = expires.Item2;
            if (info.Tier != previousTier)
            {
                socket.GetService<FlipperService>().RemoveConnection(socket);
                await AccountInfo.Update(info);
            }
            return info.Tier;
        }

        public async Task<IEnumerable<string>> GetMinecraftAccountUuids()
        {
            if (SessionInfo.MinecraftUuids.Count() > 0)
                if (AccountInfo.Value != null)
                {
                    return SessionInfo.MinecraftUuids.Concat(AccountInfo.Value.McIds).ToHashSet();
                }
                else
                    return SessionInfo.MinecraftUuids;
            var result = await socket.GetService<McAccountService>().GetAllAccounts(UserId.Value, DateTime.UtcNow - TimeSpan.FromDays(30));
            var loadSuccess = result != null;
            if (result == null || result.Count() == 0)
                result = new HashSet<string>() { SessionInfo.McUuid };
            else
            {
                /*if (AccountInfo.Value != null && AccountInfo.Value.McIds.Except(result).Any())
                {
                    AccountInfo.Value.McIds = result.ToList();
                    await AccountInfo.Update();
                }*/
            }
            if (!result.Contains(SessionInfo.McUuid))
                result = result.Append(SessionInfo.McUuid);
            if (!SessionInfo.McUuid.IsNullOrEmpty() && loadSuccess)
                SessionInfo.MinecraftUuids = result.ToHashSet();
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
            var decoded = Convert.FromBase64String(stringId);
            var sum = 0;
            for (int i = 0; i < 16; i++)
            {
                sum += decoded[i];
            }
            var newid = Convert.ToBase64String(decoded.Append((byte)(sum % 256)).ToArray());

            return $"https://sky.coflnet.com/authmod?mcid={SessionInfo.McName}&conId={HttpUtility.UrlEncode(newid)}";
        }

        public void UpdateConnectionTier(AccountInfo accountInfo, Activity span = null)
        {
            this.ConSpan.SetTag("tier", accountInfo?.Tier.ToString());
            span?.Log("set connection tier to " + accountInfo?.Tier.ToString());
            if (accountInfo == null)
                return;

            if (socket.HasFlippingDisabled())
                return;
            if (FlipSettings.Value.DisableFlips)
            {
                SendMessage(COFLNET + "you currently don't receive flips because you disabled them", "/cofl set disableflips false", "click to enable");
                return;
            }
            var tier = accountInfo.Tier;
            if (accountInfo.ExpiresAt < DateTime.UtcNow)
                tier = AccountTier.NONE;
            var flipperService = socket.GetService<FlipperService>();
            if (tier == AccountTier.NONE)
                flipperService.AddNonConnection(socket, false);
            if (tier == AccountTier.PREMIUM)
                flipperService.AddConnection(socket, false);
            else if (tier == AccountTier.PREMIUM_PLUS)
            {
                flipperService.AddConnectionPlus(socket, false);
            }
            else if (tier == AccountTier.STARTER_PREMIUM)
                flipperService.AddStarterConnection(socket, false);
            else if (tier == AccountTier.SUPER_PREMIUM)
            {
                DiHandler.GetService<PreApiService>().AddUser(socket, accountInfo.ExpiresAt);
                flipperService.AddConnectionPlus(socket, false);
                SessionInfo.captchaInfo.LastSolve = DateTime.UtcNow;
                socket.SendMessage(McColorCodes.GRAY + "speedup enabled, remaining " + (accountInfo.ExpiresAt - DateTime.UtcNow).ToString("g"));
            }
        }


        protected virtual async Task SendAuthorizedHello(AccountInfo accountInfo)
        {
            var user = UserService.Instance.GetUserById(int.Parse(accountInfo.UserId));
            var email = user.Email;
            string anonymisedEmail = UserService.Instance.AnonymiseEmail(email);
            if (this.SessionInfo.McName == null)
                await Task.Delay(800).ConfigureAwait(false); // allow another half second for the playername to be loaded
            var messageStart = $"Hello {this.SessionInfo.McName} ({anonymisedEmail}) \n";
            if (accountInfo.Tier != AccountTier.NONE && accountInfo.ExpiresAt > DateTime.UtcNow)
                SendMessage(
                    COFLNET + messageStart + $"You have {McColorCodes.GREEN}{accountInfo.Tier.ToString()} until {accountInfo.ExpiresAt.ToString("yyyy-MMM-dd HH:mm")} UTC", null,
                    $"That is in {McColorCodes.GREEN + (accountInfo.ExpiresAt - DateTime.UtcNow).ToString("d'd 'h'h 'm'm 's's'")}"
                );
            else
                SendMessage(COFLNET + messageStart + $"You use the {McColorCodes.BOLD}FREE{McColorCodes.RESET} version of the flip finder", "/cofl buy", "Click to upgrade tier");

            await Task.Delay(300).ConfigureAwait(false);
            socket.ModAdapter.OnAuthorize(accountInfo);
        }

        /// <summary>
        /// Execute every minute to clear collections
        /// </summary>
        public void HouseKeeping()
        {
            flipProcesser.MinuteCleanup();
            while (socket.TopBlocked.Count > 300)
                socket.TopBlocked.TryDequeue(out _);
            spamController.Reset();
        }

        private void SendPing()
        {
            var blockedFlipFilterCount = flipProcesser.BlockedFlipCount;
            flipProcesser.PingUpdate();
            using var span = socket.CreateActivity("ping", ConSpan)?.AddTag("count", blockedFlipFilterCount);
            try
            {
                UpdateExtraDelay();
                spamController.Reset();
                if (blockedFlipFilterCount > 0 && SessionInfo.LastBlockedMsg.AddMinutes(FlipSettings.Value.ModSettings.MinutesBetweenBlocked) < DateTime.UtcNow)
                {
                    SendBlockedMessage(blockedFlipFilterCount);
                }
                else
                {
                    socket.Send(Response.Create("ping", 0));

                    UpdateConnectionTier(AccountInfo, span);
                }
                SendReminders();
                socket.TryAsyncTimes(async () =>
                {
                    await RemoveTempFilters();
                    await AddBlacklistOfSpam();
                }, "adjust temp filters", 1);

                UpdateConnectionIfNoFlipSent(span);
                if (AccountInfo.Value?.ExpiresAt < DateTime.UtcNow && AccountInfo.Value?.ExpiresAt > DateTime.UtcNow - TimeSpan.FromMinutes(2))
                    UpdateConnectionTier(AccountInfo, span);
            }
            catch (System.InvalidOperationException)
            {
                socket.RemoveMySelf();
            }
            catch (Exception e)
            {
                span.Log("could not send ping\n" + e);
                socket.Error(e, "on ping"); // CloseBecauseError(e);
            }
        }

        private async Task AddBlacklistOfSpam()
        {
            if (FlipSettings.Value?.ModSettings?.TempBlacklistSpam == false)
                return;
            var preApiService = socket.GetService<PreApiService>();
            var toBlock = socket.LastSent.Where(s =>
                            s.Auction.Start > DateTime.UtcNow - TimeSpan.FromMinutes(3)
                            && s.TargetPrice > s.Auction.StartingBid * 2
                            && preApiService.IsSold(s.Auction.Uuid)
                        )
                        .GroupBy(s => s.Auction.Tag).Where(g => g.Count() >= 5).ToList();
            var playersToBlock = BlockPlayerBaiting(preApiService);
            foreach (var item in toBlock)
            {
                AddTempFilter(item.Key);
                socket.SendMessage(COFLNET + $"Temporarily blacklisted {item.First().Auction.ItemName} for spamming");
            }
            if (toBlock.Count > 0 || playersToBlock.Count > 0)
                await FlipSettings.Update();
        }

        private List<IGrouping<string, LowPricedAuction>> BlockPlayerBaiting(PreApiService preApiService)
        {
            var playersToBlock = socket.LastSent.Where(s =>
                                        s.Auction.Start > DateTime.UtcNow - TimeSpan.FromMinutes(3)
                                        && s.TargetPrice > s.Auction.StartingBid * 2
                                        && !preApiService.IsSold(s.Auction.Uuid)
                                    )
                                    .GroupBy(s => s.Auction.AuctioneerId).Where(g => g.Count() >= 5).ToList();
            foreach (var item in playersToBlock)
            {
                var player = item.Key;
                FlipSettings.Value.BlackList.Add(new()
                {
                    DisplayName = "Automatic blacklist",
                    filter = new(){
                    {"removeAfter", DateTime.UtcNow.AddHours(8).ToString("s")},
                    {"ForceBlacklist", "true"},
                    {"Seller", player}
                },
                });
                socket.SendMessage(COFLNET + $"Temporarily blacklisted {player} for baiting");
            }

            return playersToBlock;
        }

        private void AddTempFilter(string key)
        {
            FlipSettings.Value.BlackList.Add(new()
            {
                DisplayName = "automatic blacklist",
                ItemTag = key,
                filter = new(){
                    {"removeAfter", DateTime.UtcNow.AddHours(8).ToString("s")},
                    {"ForceBlacklist", "true"}
                },
            });
        }

        private void SendBlockedMessage(int blockedFlipFilterCount)
        {
            socket.SendMessage(new ChatPart(COFLNET + $"there were {blockedFlipFilterCount} flips blocked by your filter the last minute",
                                    "/cofl blocked",
                                    $"{McColorCodes.GRAY} execute {McColorCodes.AQUA}/cofl blocked{McColorCodes.GRAY} to list blocked flips"),
                                    new ChatPart(" ", "/cofl void", null));
            if (SessionInfo.LastBlockedMsg == default)
                socket.TryAsyncTimes(() => socket.TriggerTutorial<Sky.ModCommands.Tutorials.Blocked>(), "blocked tutorial");
            SessionInfo.LastBlockedMsg = DateTime.UtcNow;

            // remove blocked (if clear failed to do so)
            while (socket.TopBlocked.Count > 345)
                socket.TopBlocked.TryDequeue(out _);
        }

        private void UpdateConnectionIfNoFlipSent(Activity span)
        {
            if (AccountInfo == null || AccountInfo?.Value?.Tier == AccountTier.NONE)
                return;
            if (socket.LastSent.Any(s => s.Auction.Start > DateTime.UtcNow.AddMinutes(-3)))
                return; // got a flip in the last 3 minutes
            UpdateConnectionTier(AccountInfo.Value, span);
        }

        private void SendReminders()
        {
            if (AccountSettings?.Value?.Reminders == null)
                return;
            var reminders = AccountSettings?.Value?.Reminders?.Where(r => r.TriggerTime < DateTime.UtcNow).ToList();
            foreach (var item in reminders)
            {
                socket.SendSound("note.pling");
                SendMessage($"[§1R§6eminder§f]§7: " + McColorCodes.WHITE + item.Text);
                AccountSettings.Value.Reminders.Remove(item);
            }
            if (reminders?.Count > 0)
                AccountSettings.Update().Wait();
        }

        private async Task RemoveTempFilters()
        {
            var update = false;
            RemoveFilterFromList(FlipSettings.Value.WhiteList);
            RemoveFilterFromList(FlipSettings.Value.BlackList);
            if (update)
                await FlipSettings.Update();

            void RemoveFilterFromList(List<ListEntry> list)
            {
                if (list == null)
                    return;
                foreach (var filter in list
                                .Where(f => f.Tags != null
                                    && f.Tags.Any(f => f.StartsWith("removeAfter=")
                                    && DateTime.TryParse(f.Split('=').Last(), out var dt)
                                    && dt < DateTime.UtcNow)).ToList())
                {
                    socket.SendMessage(COFLNET + $"Removed filter {filter.ItemTag} because it was set to expire");
                    list.Remove(filter);
                    update = true;
                }
            }
        }

        public virtual void StartTimer(double seconds = 10, string prefix = "§c")
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
                    prefix = (mod?.TimerPrefix.IsNullOrEmpty() ?? true || prefix != "§c") ? prefix : mod.TimerPrefix,
                    maxPrecision = (mod?.TimerPrecision ?? 0) == 0 ? 3 : mod.TimerPrecision
                }));
        }

        private void UpdateExtraDelay()
        {
            socket.TryAsyncTimes(async () =>
            {
                await Task.Delay(new Random().Next(1, 3000)).ConfigureAwait(false);
                var ids = await GetMinecraftAccountUuids();
                var isBot = socket.ModAdapter is AfVersionAdapter;

                var sumary = await DelayHandler.Update(ids, LastCaptchaSolveTime);

                if (sumary.Penalty > TimeSpan.Zero)
                {
                    using var span = socket.CreateActivity("nerv", ConSpan);
                    span.Log(JsonConvert.SerializeObject(ids, Formatting.Indented));
                    span.Log(JsonConvert.SerializeObject(sumary, Formatting.Indented));
                }
                if (isBot)
                    return;
                await SendAfkWarningMessages(isBot, sumary).ConfigureAwait(false);

            }, "retrieving penalty");
        }

        private async Task SendAfkWarningMessages(bool isBot, DelayHandler.Summary sumary)
        {
            if (sumary.AntiAfk && !socket.HasFlippingDisabled() && !isBot)
            {
                if (SessionInfo.captchaInfo.LastGenerated < DateTime.UtcNow.AddMinutes(-20))
                {
                    socket.Send(Response.Create("getMods", 0));
                    await Task.Delay(1000).ConfigureAwait(false);
                    SendMessage("Hello there, you acted suspiciously like a macro bot (flipped consistently for multiple hours and/or fast). \nPlease select the correct answer to prove that you are not.", null, "You are delayed until you do");
                    SendMessage(new CaptchaGenerator().SetupChallenge(socket, SessionInfo.captchaInfo));
                }
                else if (SessionInfo.captchaInfo.LastGenerated.Minute % 4 == 1)
                {
                    socket.Dialog(db => db.CoflCommand<CaptchaCommand>($"You are currently delayed for likely being afk. Click to get a letter captcha to prove you are not.", "", "Generates a new captcha"));
                }
            }
            if (sumary.MacroWarning)
            {
                using var span = socket.CreateActivity("macroWarning", ConSpan).AddTag("name", SessionInfo.McName);
                //          SendMessage("\nWe detected macro usage on your account. \nPlease stop using any sort of unfair advantage immediately. You may be additionally and permanently delayed if you don't.");
            }
        }

        private DateTime LastCaptchaSolveTime => socket.ModAdapter is AfVersionAdapter ? DateTime.Now :
                    (AccountInfo?.Value?.LastCaptchaSolve > SessionInfo.LastCaptchaSolve ? AccountInfo.Value.LastCaptchaSolve : SessionInfo.LastCaptchaSolve);

        internal async Task SendFlipBatch(IEnumerable<LowPricedAuction> flips)
        {
            await flipProcesser.NewFlips(flips);
        }

        public void Dispose()
        {
            FlipSettings?.Dispose();
            UserId?.Dispose();
            AccountInfo?.Dispose();
            SessionInfo?.Dispose();
            PingTimer?.Dispose();
        }
    }
}
