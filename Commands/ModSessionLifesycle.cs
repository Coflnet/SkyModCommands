using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Cassandra;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.ModCommands.Tutorials;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RestSharp;
using WebSocketSharp;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Represents a mod session
    /// </summary>
    public class ModSessionLifesycle : IDisposable, IAuthUpdate
    {
        protected MinecraftSocket socket;
        public SessionInfo SessionInfo => socket.SessionInfo;
        public readonly string COFLNET = MinecraftSocket.COFLNET;
        public SelfUpdatingValue<FlipSettings> FlipSettings;
        public SelfUpdatingValue<string> UserId;
        public SelfUpdatingValue<AccountInfo> AccountInfo;
        public SelfUpdatingValue<AccountSettings> AccountSettings;
        public SelfUpdatingValue<PrivacySettings> PrivacySettings;
        public SelfUpdatingValue<ConfigContainer> LoadedConfig;
        public AccountTierManager TierManager;
        public Activity ConSpan => socket.ConSpan;
        public Timer PingTimer;
        private SpamController spamController = new SpamController();
        public IDelayHandler DelayHandler { get; set; }
        public VerificationHandler VerificationHandler;
        public FlipProcesser FlipProcessor;

        public event EventHandler<string> OnLogin;

        public TimeSpan CurrentDelay => DelayHandler?.CurrentDelay ?? MC.DelayHandler.DefaultDelay;
        public TimeSpan MacroDelay => DelayHandler?.MacroDelay ?? default;
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

        public static FlipSettings DefaultSettings => new FlipSettings()
        {
            MinProfit = 100000,
            MinVolume = 10,
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
            DelayHandler = new DelayHandler(Shared.TimeProvider.Instance, socket.GetService<FlipTrackingService>(), SessionInfo, info);

            FlipProcessor = new FlipProcesser(socket, spamController, DelayHandler);
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

    */
            using var loadSpan = socket.CreateActivity("loadSession", ConSpan);
            SessionInfo.SessionId = stringId;

            PingTimer = new Timer((_) =>
            {
                SendPing();
            }, null, TimeSpan.FromSeconds(59), TimeSpan.FromSeconds(59));
            this.TierManager = new AccountTierManager(socket, this);
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
                waitLogin.Log(GetAuthLink(stringId));
                UserId.OnChange += (newset) => Task.Run(async () => await SubToSettings(newset));
                FlipSettings = await SelfUpdatingValue<FlipSettings>.CreateNoUpdate(() => DefaultSettings);
                SubSessionToEventsFor(SessionInfo.McUuid);
            }
            else
            {
                using var sub2SettingsSpan = socket.CreateActivity("sub2Settings", ConSpan);
                await SubToSettings(UserId);
            }

            loadSpan?.Dispose();
            UpdateExtraDelay();
            TierManager.OnTierChange += (s, Newtier) =>
            {
                Console.WriteLine("tier changed to " + Newtier);
                socket.SessionInfo.SessionTier = Newtier;
                UpdateConnectionTier(Newtier);
            };
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
            OnLogin?.Invoke(this, userId);
            ConSpan.Log("subbing to settings of " + userId);
            var flipSettingsTask = SelfUpdatingValue<FlipSettings>.Create(userId, "flipSettings", () => DefaultSettings);
            var accountSettingsTask = SelfUpdatingValue<AccountSettings>.Create(userId, "accountSettings", () => new());
            Activity.Current.Log("got settings");
            AccountInfo = await SelfUpdatingValue<AccountInfo>.Create(userId, "accountInfo", () => new AccountInfo() { UserId = userId });
            Activity.Current.Log("got accountInfo");
            var oldSettings = FlipSettings;
            FlipSettings = await flipSettingsTask ??
                throw new Exception("flipSettings is null");
            Activity.Current.Log("got flipSettings");
            oldSettings?.Dispose();
            if (FlipSettings?.Value == null)
                throw new Exception("flipSettings.Value is null");

            SetActiveConIdToCurrent();
            Activity.Current.Log("single connection check");
            FlipSettings.OnChange += UpdateSettings;
            FlipSettings.ShouldPreventUpdate = (fs) => fs?.Changer == SessionInfo.ConnectionId;
            AccountInfo.OnChange += (ai) => Task.Run(async () => await UpdateAccountInfo(ai), new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);
            if (AccountInfo.Value != default)
                await UpdateAccountInfo(AccountInfo);
            else
                Console.WriteLine("accountinfo is default");
            Activity.Current.Log("updated accountInfo");
            SetupFlipProcessor(AccountInfo);
            AccountSettings = await accountSettingsTask;
            if (userId != null)
                SubSessionToEventsFor(userId);
            Activity.Current.Log("subbed to events");
            await ApplyFlipSettings(FlipSettings.Value, ConSpan);
            Activity.Current.Log("applied flip settings");
            await socket.TryAsyncTimes(SubToConfigChanges, "config subscribe");
        }

        public async Task SubToConfigChanges()
        {
            using var span = socket.CreateActivity("subToConfigChanges", ConSpan);
            if (AccountSettings.Value == null)
                await AccountSettings.Update(new AccountSettings());
            var loadedConfigMetadata = AccountSettings.Value.LoadedConfig;
            span.Log("loaded config " + loadedConfigMetadata?.Name);
            if (loadedConfigMetadata != null)
            {
                LoadedConfig = await SelfUpdatingValue<ConfigContainer>.Create(loadedConfigMetadata.OwnerId, SellConfigCommand.GetKeyFromname(loadedConfigMetadata.Name), () => throw new Exception("config not found"));
                span.Log("got config " + LoadedConfig?.Value?.Name);
                if (LoadedConfig.Value != null)
                {
                    var newConfig = LoadedConfig.Value;
                    ShowConfigUpdateOption(loadedConfigMetadata, newConfig);
                    LoadedConfig.OnChange += (config) => ShowConfigUpdateOption(loadedConfigMetadata, config);
                }
            }

            void ShowConfigUpdateOption(OwnedConfigs.OwnedConfig loadedConfigMetadata, ConfigContainer newConfig)
            {
                span.Log($"new config {newConfig.Name} {newConfig.Version} > {loadedConfigMetadata.Version}");
                if (newConfig.Version > loadedConfigMetadata.Version)
                {
                    socket.Dialog(db => db.MsgLine($"Your config: §6{newConfig.Name} §7v{loadedConfigMetadata.Version} §6updated to v{newConfig.Version}")
                        .MsgLine($"§7{newConfig.ChangeNotes}")
                        .If(() => AccountSettings.Value.AutoUpdateConfig, db => db.MsgLine("Loading the updated version automatically.").Msg("To toggle this run /cofl configs autoupdate").AsGray(),
                        db => db.CoflCommand<LoadConfigCommand>($"[click to load]", $"{newConfig.OwnerId} {newConfig.Name}", "load new version\nWill override your current settings")));
                    if (AccountSettings.Value.AutoUpdateConfig)
                    {
                        socket.ExecuteCommand("/cofl loadconfig " + newConfig.OwnerId + " " + newConfig.Name);
                    }
                }
            }
        }

        /// <summary>
        ///  make sure there is only one connection
        /// </summary>
        private void SetActiveConIdToCurrent()
        {
            _ = socket.TryAsyncTimes(async () =>
            {
                using var span = socket.CreateActivity("updateAccountInfo", ConSpan);
                AccountInfo.Value.ActiveConnectionId = SessionInfo.ConnectionId;
                await AccountInfo.Update(AccountInfo.Value);
                span.AddTag("activeConId", SessionInfo.ConnectionId);
            }, "accountInfo update");
        }

        private void SubSessionToEventsFor(string val)
        {
            SessionInfo.EventBrokerSub?.Unsubscribe();
            SessionInfo.EventBrokerSub = socket.GetService<EventBrokerClient>().SubEvents(val, onchange =>
            {
                SendMessage(COFLNET + onchange.Message);
            });
        }

        /// <summary>
        /// Makes sure given settings are applied
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="span"></param>
        public async Task ApplyFlipSettings(FlipSettings settings, Activity span)
        {
            if (settings == null)
                return;
            var testFlip = BlacklistCommand.GetTestFlip("test");
            try
            {
                if (settings.BasedOnLBin && settings.AllowedFinders != LowPricedAuction.FinderType.SNIPER)
                {
                    socket.Dialog(db => db.CoflCommand<SetCommand>(McColorCodes.RED + "Your profit is based on lbin, therefore you should only use the `sniper` flip finder to maximise speed", "finders sniper", "Click to only use the sniper"));
                    _ = socket.TryAsyncTimes(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        socket.Dialog(db => db.LineBreak().MsgLine(McColorCodes.RED + "Having profit set to lbin with other finders may lead to negative price estimations being displayed if you whitelisted an item"));
                    }, "sniper warning");
                }
                if (settings.Visibility.LowestBin && settings.AllowedFinders != LowPricedAuction.FinderType.SNIPER && !SessionInfo.LbinWarningSent)
                {
                    SessionInfo.LbinWarningSent = true;
                    socket.SendMessage(new DialogBuilder().CoflCommand<SetCommand>(McColorCodes.RED + "You enabled display of lbin on a flip finder that is not based on lbin (but median). " +
                        "That slows down flips because the lbin has to be searched for before the flip is sent to you." +
                        "If you are okay with that, ignore this warning. If you want flips faster click this to disable displaying the lbin",
                        "showlbin false",
                        $"You can also enable only lbin based flips \nby executing {McColorCodes.AQUA}/cofl set finders sniper.\nClicking this will hide lbin in flip messages. \nYou can still see lbin in item descriptions."));
                }
                settings.CopyListMatchers(FlipSettings);
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

        public void CheckListValidity(FlipInstance testFlip, List<ListEntry> blacklist, bool whiteList = false)
        {
            foreach (var item in blacklist.ToList())
            {
                try
                {
                    var expression = item.GetExpression();
                    expression.Compile()(testFlip);
                }
                catch (Exception e)
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
            CreateBackupIfVeryDifferent(settings);
            span?.AddTag("changed", changed);
            ApplyFlipSettings(settings, span).Wait();
        }

        /// <summary>
        /// Creates a backup if the settings are very different from the current ones
        /// </summary>
        /// <param name="settings"></param>
        private void CreateBackupIfVeryDifferent(FlipSettings settings)
        {
            var current = FlipSettings.Value;
            var newSettings = settings;
            var diffCount = 0;
            if (current?.Visibility?.Volume != newSettings?.Visibility?.Volume)
                diffCount++;
            if (current?.WhiteList?.Count != newSettings?.WhiteList?.Count)
                diffCount++;
            if (current?.BlackList?.Count != newSettings?.BlackList?.Count)
                diffCount++;
            if (diffCount >= 2)
            {
                socket.TryAsyncTimes(async () =>
                {
                    socket.Dialog(db => db.MsgLine("Seems like you imported a different config, creating a backup of your current one")
                        .CoflCommand<BackupCommand>("Click to see your backups", "ls", "Click to see your backups\nRuns /cofl backup list"));
                    var backups = await BackupCommand.GetBackupList(socket);
                    var name = "Before last import";
                    backups.RemoveAll(b => b.Name == name);
                    backups.Add(new BackupEntry() { Name = name, settings = current });
                    await BackupCommand.SaveBackupList(socket, backups);
                }, "multiple settings warning");
            }
        }

        protected virtual async Task UpdateAccountInfo(AccountInfo info)
        {
            using var span = socket.CreateActivity("AuthUpdate", ConSpan)?
                .AddTag("premium", (await TierManager.GetCurrentCached()).ToString())
                .AddTag("userId", info.UserId);
            if (socket.IsClosed)
            {
                span?.Log("socket is closed");
                return;
            }
            try
            {
                var userIsVerifiedTask = VerificationHandler.MakeSureUserIsVerified(info, socket.SessionInfo);
                span.Log(JsonConvert.SerializeObject(info, Formatting.Indented));
                if (info.UserId.IsNullOrEmpty())
                {
                    info.UserId = socket.UserId;
                    await AccountInfo.Update(info);
                }
                var userIsTest = info.UserIdOld > 0 && info.UserIdOld < 10;
                if (info.ActiveConnectionId != SessionInfo.ConnectionId && !string.IsNullOrEmpty(info.ActiveConnectionId) && !userIsTest)
                {
                    // wait for settings sync
                    await Task.Delay(4500).ConfigureAwait(false);
                    var currentId = AccountInfo?.Value?.ActiveConnectionId;
                    if (currentId != SessionInfo.ConnectionId)
                    {
                        // another connection of this account was opened, close this one
                        SendMessage("\n\n" + COFLNET + McColorCodes.GREEN + "Another connection was opened, this one may be downgraded to the free version if no license is present", null,
                            "Licenses allow you to use premium on multiple minecraft accounts at the same time.\nSee /cofl licenses for more information");

                    }
                }

                if (info.ConIds.Contains("logout"))
                {
                    SendMessage("You have been logged out");
                    span.Log("force loggout");
                    info.ConIds.Remove("logout");
                    await AccountInfo.Update(info);
                    socket.Close();
                    return;
                }
                var tier = await TierManager.GetCurrentCached();

                if (SessionInfo.SentWelcome)
                    return; // don't send hello again
                SessionInfo.SentWelcome = true;
                await SendAuthorizedHello(info);

                await WaitForSettingsLoaded(span);
                if (FlipSettings.Value.ModSettings.AutoStartFlipper)
                {
                    SendMessage(socket.formatProvider.WelcomeMessage());
                    SessionInfo.FlipsEnabled = true;
                    UpdateConnectionTier(tier, span);
                    span?.AddTag("autoStart", "true");
                    await PrintRegionInfo(info);
                }
                else if (!FlipSettings.Value.ModSettings.AhDataOnlyMode)
                {
                    socket.Dialog(db => db.Msg("What do you want to do?").Break
                        .CoflCommand<FlipCommand>($"> {McColorCodes.GOLD}AH flip  ", "true", $"{McColorCodes.GOLD}Show me flips!\n{McColorCodes.DARK_GREEN}(and reask on every start)\nexecutes {McColorCodes.AQUA}/cofl flip")
                        .CoflCommand<FlipCommand>(McColorCodes.DARK_GREEN + " always ah flip ", "always", McColorCodes.DARK_GREEN + "don't show this again and always show me flips")
                        .DialogLink<FlipDisableDialog>(McColorCodes.BLUE + " use the pricing data ", "never", "I don't want to flip")
                        .Break);
                    await socket.TriggerTutorial<Welcome>();
                    span?.AddTag("autoStart", "false");
                }
                await userIsVerifiedTask;
                socket.Send(Response.Create("loggedIn", new { uuid = SessionInfo.McUuid, verified = SessionInfo.VerifiedMc }));

                if (DateTime.Now < new DateTime(2024, 4, 2))
                {
                    socket.Dialog(db =>
                        db.MsgLine($"{McColorCodes.BOLD}Happy Easter! {McColorCodes.OBFUSCATED}!!")
                        .CoflCommand<PurchaseCommand>($"We got a special {McColorCodes.AQUA}100 days prem+ offer{McColorCodes.RESET} for {McColorCodes.RED}26% cheaper{McColorCodes.RESET} than buying it weekly {McColorCodes.YELLOW}(click)", "premium_plus-100", "Click to buy 100 days prem+ for 26% off"));
                }
            }
            catch (Exception e)
            {
                socket.Error(e, "loading modsocket");
                span.AddTag("error", true);
                SendMessage(COFLNET + $"Your settings could not be loaded, please relink again :)");
            }
        }

        private async Task PrintRegionInfo(AccountInfo info)
        {
            if (TierManager.HasAtLeast(AccountTier.PREMIUM_PLUS) && SessionInfo.ConnectionType == null)
            {
                if ((socket.CurrentRegion == info.Region || info.Region == null) && info.Locale == "en")
                    socket.Dialog(db => db.CoflCommand<SwitchRegionCommand>(McColorCodes.GRAY + "Switching region is now done with /cofl switchregion <region>", "", "Click to see region options"));
                else if (SessionInfo.IsMacroBot && socket.Version.StartsWith("1.5.0"))
                {
                    socket.Dialog(db => db.MsgLine("Your client doesn't seem to support switching regions"));
                }
                else if (info.Region == "us" && !MinecraftSocket.IsDevMode)
                {
                    // check if reachable
                    await SwitchRegionCommand.TryToConnect(socket);

                }
            }
            else if (info.Region == "eu" && SessionInfo.ConnectionType != null)
            {
                socket.Dialog(db => db.MsgLine("Switching to eu server"));
                socket.ExecuteCommand("/cofl connect wss://sky.coflnet.com/modsocket");
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

        public async Task<IEnumerable<string>> GetMinecraftAccountUuids()
        {
            if (SessionInfo.MinecraftUuids.Count() > 0)
                if (AccountInfo.Value != null)
                {
                    return SessionInfo.MinecraftUuids.Concat(AccountInfo.Value.McIds).ToHashSet();
                }
                else
                    return SessionInfo.MinecraftUuids;
            var result = (await socket.GetService<McAccountService>()
                .GetAllAccounts(UserId.Value, DateTime.UtcNow - TimeSpan.FromDays(60))).ToHashSet();
            var loadSuccess = result.Any();
            result.Add(SessionInfo.McUuid);
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

        public void UpdateConnectionTier(AccountTier tier, Activity span = null)
        {
            ConSpan.SetTag("tier", tier.ToString());
            if (socket.HasFlippingDisabled())
                return;
            if (FlipSettings.Value.DisableFlips)
            {
                SendMessage(COFLNET + "you currently don't receive flips because you disabled them", "/cofl set disableflips false", "click to enable");
                return;
            }
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
                DiHandler.GetService<PreApiService>().AddUser(socket, DateTime.Now + TimeSpan.FromMinutes(10));
                flipperService.AddConnectionPlus(socket, false);
                SessionInfo.captchaInfo.LastSolve = DateTime.UtcNow;
                socket.SendMessage(McColorCodes.GRAY + "speedup enabled, remaining " + (TierManager.ExpiresAt - DateTime.UtcNow).ToString("g"));
            }
        }

        protected virtual async Task SendAuthorizedHello(AccountInfo accountInfo)
        {
            var user = UserService.Instance.GetUserById(int.Parse(accountInfo.UserId));
            var email = user.Email;
            string anonymisedEmail = UserService.Instance.AnonymiseEmail(email);
            if (SessionInfo.McName == null)
                await Task.Delay(800).ConfigureAwait(false); // allow another half second for the playername to be loaded
            var messageStart = $"Hello {SessionInfo.McName} ({anonymisedEmail}) \n";
            Console.WriteLine("getting tier cached");
            (var tier, var expire) = await this.TierManager.GetCurrentTierWithExpire();
            socket.SessionInfo.SessionTier = tier;
            Console.WriteLine("tier: " + tier);
            if (tier != AccountTier.NONE)
                SendMessage(
                    COFLNET + messageStart + $"You have {McColorCodes.GREEN}{tier} until {expire.ToString("yyyy-MMM-dd HH:mm")} UTC", null,
                    $"That is in {McColorCodes.GREEN + (expire - DateTime.UtcNow).ToString("d'd 'h'h 'm'm 's's'")}"
                );
            else
            {
                SendMessage(COFLNET + messageStart + $"You use the {McColorCodes.BOLD}FREE{McColorCodes.RESET} version of the flip finder", "/cofl buy", "Click to upgrade tier");
                if (TierManager.IsConnectedFromOtherAccount(out var otherUUid, out var userTier) && userTier != AccountTier.NONE)
                {
                    var name = await socket.GetPlayerName(otherUUid);
                    socket.Dialog(di => di
                        .Msg($"You are using your {userTier} on a the account with the name {McColorCodes.AQUA}{name}",
                            "/cofl licenses default " + SessionInfo.McName, "Click to change use it on this account"));
                }
            }
            socket.ModAdapter.OnAuthorize(accountInfo);
        }

        /// <summary>
        /// Execute every minute to clear collections
        /// </summary>
        public void HouseKeeping()
        {
            FlipProcessor.MinuteCleanup();
            while (socket.TopBlocked.Count > 300)
                socket.TopBlocked.TryDequeue(out _);
            spamController.Reset();
        }

        private void SendPing()
        {
            var blockedFlipFilterCount = FlipProcessor.BlockedFlipCount;
            FlipProcessor.PingUpdate();
            using var span = socket.CreateActivity("ping", ConSpan)?.AddTag("count", blockedFlipFilterCount);
            try
            {
                UpdateExtraDelay();
                spamController.Reset();
                if (blockedFlipFilterCount > 0 && SessionInfo.LastBlockedMsg.AddMinutes(FlipSettings.Value?.ModSettings?.MinutesBetweenBlocked ?? 0) < DateTime.UtcNow)
                {
                    SendBlockedMessage(blockedFlipFilterCount);
                }
                else
                {
                    socket.Send(Response.Create("ping", 0));
                }
                SendReminders();
                socket.TryAsyncTimes(async () =>
                {
                    await RemoveTempFilters();
                    await AddBlacklistOfSpam();
                }, "adjust temp filters", 1);

                UpdateConnectionIfNoFlipSent(span);
            }
            catch (InvalidOperationException)
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
            var preApiService = socket.GetService<PreApiService>();
            var badSellers =
                socket.LastSent.Where(s => s.TargetPrice > s.Auction.StartingBid * 9
                            && !preApiService.IsSold(s.Auction.Uuid))
                .GroupBy(s => s.Auction.AuctioneerId + s.Auction.Tag).Where(g => g.Count() >= 3)
                .ToList();
            if (badSellers.Any())
            {
                foreach (var item in badSellers)
                {
                    if (FlipSettings.Value.BlackList.Any(b => b.ItemTag == item.First().Auction.Tag))
                        continue;
                    FlipSettings.Value.BlackList.Add(new()
                    {
                        DisplayName = "Automatic blacklist of " + item.First().Auction.ItemName,
                        ItemTag = item.First().Auction.Tag,
                        filter = new()
                            { { "Seller", item.First().Auction.AuctioneerId }
                            },
                        Tags = new List<string>() { "removeAfter=" + DateTime.UtcNow.AddHours(48).ToString("s") }
                    });
                    socket.Dialog(db => db.CoflCommand<BlacklistCommand>(
                        $"Temporarily blacklisted {item.First().Auction.ItemName} from {item.First().Auction.AuctioneerId} for baiting",
                        $"rm {item.First().Auction.Tag}",
                        "click to remove again"));
                }
                await FlipSettings.Update();
                FlipSettings.Value.RecompileMatchers();
            }
            if (FlipSettings.Value?.ModSettings?.TempBlacklistSpam == false)
                return;
            var toBlock = socket.LastSent.Where(s =>
                    s.Auction.Start > DateTime.UtcNow - TimeSpan.FromMinutes(3) &&
                    s.TargetPrice > s.Auction.StartingBid * 2 &&
                    !preApiService.IsSold(s.Auction.Uuid)
                )
                .GroupBy(s => s.Auction.Tag).Where(g => g.Count() >= 10).ToList();
            var playersToBlock = BlockPlayerBaiting(preApiService);
            foreach (var item in toBlock)
            {
                AddTempFilter(item.Key);
                socket.SendMessage(COFLNET + $"Temporarily blacklisted {item.First().Auction.ItemName} for spamming");
            }
            if (toBlock.Count > 0 || playersToBlock.Count > 0)
            {
                await FlipSettings.Update();
                FlipSettings.Value.RecompileMatchers();
            }
        }

        private List<IGrouping<string, LowPricedAuction>> BlockPlayerBaiting(PreApiService preApiService)
        {
            var playersToBlock = socket.LastSent.Where(s =>
                    s.Auction.Start > DateTime.UtcNow - TimeSpan.FromMinutes(3) &&
                    s.TargetPrice > s.Auction.StartingBid * 2 &&
                    !preApiService.IsSold(s.Auction.Uuid)
                )
                .GroupBy(s => s.Auction.AuctioneerId).Where(g => g.Count() >= 5).ToList();
            foreach (var item in playersToBlock)
            {
                var player = item.Key;
                FlipSettings.Value.BlackList.Add(new()
                {
                    DisplayName = "Automatic blacklist",
                    filter = new()
                        { { "ForceBlacklist", "true" }, { "Seller", player }
                        },
                    Tags = new List<string>() { "removeAfter=" + DateTime.UtcNow.AddHours(8).ToString("s") }
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
                filter = new()
                    {  { "ForceBlacklist", "true" }
                    },
                Tags = new List<string>() { "removeAfter=" + DateTime.UtcNow.AddHours(8).ToString("s") }
            });
        }

        private void SendBlockedMessage(int blockedFlipFilterCount)
        {
            socket.SendMessage(new ChatPart(COFLNET + $"Your filter blocked {blockedFlipFilterCount} in the last minute",
                    "/cofl blocked",
                    $"{McColorCodes.GRAY} execute {McColorCodes.AQUA}/cofl blocked{McColorCodes.GRAY} to list blocked flips\n" +
                    $"{McColorCodes.GRAY}This message is meant to show you that there were flips\n" +
                    $"{McColorCodes.GRAY}You can make it show less within /cofl set 3"),
                new ChatPart(" ", "/cofl void"));
            if (SessionInfo.LastBlockedMsg == default)
                socket.TryAsyncTimes(() => socket.TriggerTutorial<Blocked>(), "blocked tutorial");
            SessionInfo.LastBlockedMsg = DateTime.UtcNow;

            // remove blocked (if clear failed to do so)
            while (socket.TopBlocked.Count > 345)
                socket.TopBlocked.TryDequeue(out _);
        }

        private void UpdateConnectionIfNoFlipSent(Activity span)
        {
            if (socket.LastSent.Any(s => s.Auction.Start > DateTime.UtcNow.AddMinutes(-3)))
                return; // got a flip in the last 3 minutes

            socket.TryAsyncTimes(async () =>
            {
                var tier = await TierManager.GetCurrentCached();
                UpdateConnectionTier(tier, span);
            }, "resub to flips");
        }

        private void SendReminders()
        {
            var reminders = AccountSettings?.Value?.Reminders?.Where(r => r.TriggerTime < DateTime.UtcNow).ToList();
            if (reminders == null)
                return;
            foreach (var item in reminders)
            {
                socket.SendSound("note.pling");
                SendMessage($"[§1R§6eminder§f]§7: " + McColorCodes.WHITE + item.Text);
                AccountSettings?.Value?.Reminders?.Remove(item);
            }
            if (reminders.Count > 0)
                AccountSettings?.Update().Wait();
        }

        private async Task RemoveTempFilters()
        {
            var update = false;
            RemoveFilterFromList(FlipSettings.Value.WhiteList);
            RemoveFilterFromList(FlipSettings.Value.BlackList);
            if (update)
            {
                await FlipSettings.Update();
                FlipSettings.Value.RecompileMatchers();
            }
            void RemoveFilterFromList(List<ListEntry> list)
            {
                if (list == null)
                    return;
                foreach (var filter in list
                        .Where(f => f.Tags != null &&
                            f.Tags.Any(s => s != null && s.StartsWith("removeAfter=") &&
                                DateTime.TryParse(s.Split('=').Last(), out var dt) &&
                                dt < DateTime.UtcNow)
                                || (f.filter?.ContainsKey("removeAfter") ?? false)
                                ).ToList())
                {
                    socket.SendMessage(COFLNET + $"Removed filter {filter.ItemTag} because it was set to expire");
                    list.Remove(filter);
                    update = true;
                }
            }
        }

        public virtual void StartTimer(double seconds = 10, string prefix = "§c")
        {
            var mod = FlipSettings.Value?.ModSettings;
            if (socket.Version == "1.3-Alpha")
                socket.SendMessage(COFLNET + "You have to update your mod to support the timer");
            else
                socket.Send(Response.Create("countdown", new
                {
                    seconds,
                    widthPercent = (mod?.TimerX ?? 0) == 0 ? 10 : mod.TimerX,
                    heightPercent = (mod?.TimerY ?? 0) == 0 ? 10 : mod.TimerY,
                    scale = (mod?.TimerScale ?? 0) == 0 ? 2 : mod.TimerScale,
                    prefix = ((mod?.TimerPrefix.IsNullOrEmpty() ?? true) || prefix != "§c") ? prefix : mod.TimerPrefix,
                    maxPrecision = (mod?.TimerPrecision ?? 0) == 0 ? 3 : mod.TimerPrecision
                }));
        }

        private void UpdateExtraDelay()
        {
            socket.TryAsyncTimes(async () =>
            {
                await Task.Delay(new Random().Next(1, 3000)).ConfigureAwait(false);
                if (socket.HasFlippingDisabled())
                    return;
                if (AccountInfo.Value.ShadinessLevel == -1 && SessionInfo.VerifiedMc && TierManager.HasAtLeast(AccountTier.PREMIUM))
                {
                    try
                    {
                        var altProb = await socket.GetService<AltChecker>().AltLevel(SessionInfo.McUuid);
                        AccountInfo.Value.ShadinessLevel = altProb;
                    }
                    catch (System.Exception e)
                    {
                        socket.Error(e, "getting alt level");
                    }
                }
                var ids = await GetMinecraftAccountUuids();
                var isBot = socket.ModAdapter is AfVersionAdapter;

                var summary = await DelayHandler.Update(ids, LastCaptchaSolveTime);
                SessionInfo.NotPurchaseRate = summary.nonpurchaseRate;

                if (summary.Penalty > TimeSpan.Zero)
                {
                    using var span = socket.CreateActivity("nerv", ConSpan);
                    span.Log(JsonConvert.SerializeObject(ids, Formatting.Indented));
                    span.Log(JsonConvert.SerializeObject(summary, Formatting.Indented));
                }
                if (summary.HasBadPlayer && Random.Shared.NextDouble() < 0.1)
                {
                    await SendShitFlip();
                }
                if (isBot)
                    return;
                await SendAfkWarningMessages(summary).ConfigureAwait(false);

            }, "retrieving penalty");
        }

        private async Task SendShitFlip()
        {
            var itemIds = new List<int>() { 10521, 1306, 1249, 2338, 1525, 1410, 3000, 1271, 6439 }; // good luck figuring those out
            using var context = new HypixelContext();
            var start = DateTime.UtcNow - TimeSpan.FromMinutes(1);
            var maxId = context.Auctions.Max(a => a.Id);
            var auctions = context.Auctions.Where(a =>
                a.Id > maxId - 1000 && a.End > DateTime.UtcNow
                && a.Start > start
                && a.HighestBidAmount == 0 && a.StartingBid > 15_000_000)
                .Include(a => a.NbtData).Include(a => a.Enchantments).Take(50).ToList();


            var sniperService = socket.GetService<ISniperClient>();
            var values = await sniperService.GetPrices(auctions);
            var combined = auctions.Zip(values, (a, v) => new { Auction = a, Value = v }).ToList();
            foreach (var item in combined.OrderByDescending(c => c.Auction.StartingBid - c.Value.Median).Take(5).OrderByDescending(a => Random.Shared.Next()).Take(2))
            {
                var auction = item.Auction;
                if (Random.Shared.NextDouble() < 0.3)
                    auction.Context["cname"] = auction.ItemName + McColorCodes.DARK_GRAY + "!";
                else if (Random.Shared.NextDouble() < 0.3)
                    auction.Context["cname"] = auction.ItemName + McColorCodes.GRAY + "-us";
                var loss = auction.StartingBid - item.Value.Median;
                using var sendSpan = socket.CreateActivity("shitItem", ConSpan)
                        ?.AddTag("auctionId", auction.Uuid)?.AddTag("ip", socket.ClientIp)
                        .AddTag("estLoss", loss).AddTag("options", auctions.Count);
                if (loss < 10_000_000)
                    continue;
                var tracker = DiHandler.GetService<CircumventTracker>();
                await tracker.SendChallangeFlip(socket, FlipperService.LowPriceToFlip(new LowPricedAuction()
                {
                    Auction = auction,
                    Finder = LowPricedAuction.FinderType.SNIPER,
                    DailyVolume = (float)(1 + Random.Shared.NextDouble() * 10),
                    TargetPrice = (long)(auction.StartingBid * (1.1 + Random.Shared.NextDouble()))
                }));
                await Task.Delay(Random.Shared.Next(500, 10000));
            }

        }

        private async Task SendAfkWarningMessages(DelayHandler.Summary sumary)
        {
            if (socket.HasFlippingDisabled() || socket.CurrentRegion != "eu")
                return;
            var recentlySwitchedFromMarco = AccountInfo?.Value?.LastMacroConnect > DateTime.UtcNow.AddDays(-2);
            if (recentlySwitchedFromMarco && !sumary.AntiAfk && SessionInfo.captchaInfo.LastSolve < DateTime.UtcNow.AddMinutes(-120)
                && SessionInfo.captchaInfo.LastGenerated < DateTime.UtcNow.AddMinutes(-3)
                && AccountInfo?.Value?.LastCaptchaSolve < DateTime.UtcNow.AddMinutes(-30)
                )
            {
                SessionInfo.IsMacroBot = true;
                if (AccountInfo?.Value?.LastMacroConnect > DateTime.UtcNow.AddHours(-12))
                {
                    SendMessage("You were recently found to be afk macroing. \nTo proof that you are a human please solve this captcha.\nAlternatively click this to disable flips", "/cofl flip off", "disable flips until reconnect");
                    SendMessage(new CaptchaGenerator().SetupChallenge(socket, SessionInfo.captchaInfo));
                }
                return;
            }
            if (sumary.AntiAfk)
            {
                if (SessionInfo.captchaInfo.LastGenerated < DateTime.UtcNow.AddMinutes(-20))
                {
                    socket.Send(Response.Create("getMods", 0));
                    await Task.Delay(1000).ConfigureAwait(false);
                    SendMessage("Hello there, you acted suspiciously like a macro bot (flipped consistently for multiple hours and/or fast). \nPlease select the correct answer to prove that you are not.", null, "You are delayed until you do");
                    SendMessage(new CaptchaGenerator().SetupChallenge(socket, SessionInfo.captchaInfo));
                    await socket.TriggerTutorial<CaptchaTutorial>();
                }
                else if (SessionInfo.captchaInfo.LastGenerated.Minute % 4 == 1)
                {
                    socket.Dialog(db => db.CoflCommand<CaptchaCommand>($"You are currently delayed for likely being afk. Click to get a letter captcha to prove you are not.", "", "Generates a new captcha"));
                }
            }
            if (sumary.MacroWarning)
            {
                using var span = socket.CreateActivity("macroWarning", ConSpan)?.AddTag("name", SessionInfo.McName);
                //          SendMessage("\nWe detected macro usage on your account. \nPlease stop using any sort of unfair advantage immediately. You may be additionally and permanently delayed if you don't.");
            }
        }

        private DateTime LastCaptchaSolveTime => socket.ModAdapter is AfVersionAdapter ? DateTime.Now :
            (AccountInfo?.Value?.LastCaptchaSolve > SessionInfo.LastCaptchaSolve ? AccountInfo.Value.LastCaptchaSolve : SessionInfo.LastCaptchaSolve);

        internal async Task SendFlipBatch(IEnumerable<LowPricedAuction> flips)
        {
            await FlipProcessor.NewFlips(flips);
        }

        public void Dispose()
        {
            FlipSettings?.Dispose();
            UserId?.Dispose();
            AccountInfo?.Dispose();
            SessionInfo?.Dispose();
            AccountSettings?.Dispose();
            LoadedConfig?.Dispose();
            PingTimer?.Dispose();
            TierManager?.Dispose();
        }
    }

    public interface IAuthUpdate
    {
        event EventHandler<string> OnLogin;
    }
}