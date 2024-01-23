using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using System.Diagnostics;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;
using Microsoft.Extensions.DependencyInjection;
using Coflnet.Sky.ModCommands.Dialogs;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading;
#nullable enable
namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Main connection point for the mod.
    /// Handles establishing, authorization and handling of messages for a session
    /// </summary>
    public class MinecraftSocket : WebSocketBehavior, IFlipConnection, IMinecraftSocket
    {
        public static string COFLNET = "[§1C§6oflnet§f]§7: ";
        public virtual string CurrentRegion => "eu";

        public long Id { get; private set; }

        public SessionInfo SessionInfo { get; protected set; } = new SessionInfo();

        public FlipSettings Settings => sessionLifesycle.FlipSettings;
        public AccountInfo AccountInfo => sessionLifesycle?.AccountInfo;

        public string Version { get; private set; }
        public ActivitySource tracer => DiHandler.ServiceProvider.GetRequiredService<ActivitySource>();
        public Activity ConSpan { get; private set; }
        public IModVersionAdapter ModAdapter;

        public FormatProvider formatProvider { get; private set; }
        public ModSessionLifesycle sessionLifesycle { get; protected set; }

        public static bool IsDevMode { get; } = System.Net.Dns.GetHostName().Contains("ekwav");
        public string ClientIp => Headers["X-Real-Ip"] ?? UserEndPoint.Address.ToString();

        public static readonly ClassNameDictonary<McCommand?> Commands = new ClassNameDictonary<McCommand?>();

        public readonly ConcurrentDictionary<string, FlipInstance> ReceivedConfirm = new();

        public static event Action NextUpdateStart;
        /// <summary>
        /// The time flips are expected to come in
        /// </summary>
        protected static DateTime NextFlipTime { get; set; }

        //int IFlipConnection.UserId => int.Parse(sessionLifesycle?.UserId?.Value ?? "0");
        public string UserId
        {
            get
            {
                if (IsDevMode)
                    return "1";
                if (sessionLifesycle?.UserId?.Value == null)
                    throw new NoLoginException();
                return sessionLifesycle.UserId.Value;
            }
        }

        private static Timer tenSecTimer;
        private static IAhActive ahActive => DiHandler.GetService<IAhActive>();
        public IAhActive AhActive => ahActive;

        public ConcurrentQueue<BlockedElement> TopBlocked = new ConcurrentQueue<BlockedElement>();
        public ConcurrentQueue<LowPricedAuction> LastSent { get; } = new ConcurrentQueue<LowPricedAuction>();
        /// <summary>
        /// Triggered when the connection closes
        /// </summary>
        public event Action? OnConClose;

        public class BlockedElement
        {
            public LowPricedAuction Flip = null!;
            public string Reason = null!;
            public DateTime Now = DateTime.UtcNow;
        }

        public MinecraftSocket()
        {
            formatProvider = new FormatProvider(this);
        }

        static MinecraftSocket()
        {
            Commands.Add<TestCommand>();
            Commands.Add<SoundCommand>();
            Commands.Add<ReferenceCommand>();
            Commands.Add<ReportCommand>();
            Commands.Add<PurchaseStartCommand>();
            Commands.Add<PurchaseConfirmCommand>();
            Commands.Add<ClickedCommand>();
            Commands.Add<TrackCommand>();
            Commands.Add<ResetCommand>();
            Commands.Add<OnlineCommand>();
            Commands.Add<DelayCommand>();
            Commands.Add<DebugSCommand>();
            Commands.Add<FResponseCommand>();
            Commands.Add<DerpyCommand>();
            Commands.Add<BlacklistCommand>("bl");
            Commands.Add<WhitelistCommand>("wl");
            Commands.Add<MuteCommand>();
            Commands.Add<UnMuteCommand>();
            Commands.Add<VoidCommand>();
            Commands.Add<BlockedCommand>();
            Commands.Add<ChatCommand>("c");
            Commands.Add<RateCommand>();
            Commands.Add<TimeCommand>();
            Commands.Add<DialogCommand>();
            Commands.Add<ProfitCommand>();
            Commands.Add<LoadFlipHistory>();
            Commands.Add<LeaderboardCommand>("lb");
            Commands.Add<LoserboardCommand>();
            Commands.Add<BuyspeedboardCommand>();
            Commands.Add<TradesCommand>();
            Commands.Add<FlipsCommand>();
            Commands.Add<AhOpenCommand>();
            Commands.Add<GetMcNameForCommand>();
            Commands.Add<SetCommand>("s");
            Commands.Add<GetCommand>();
            Commands.Add<TopUpCommand>();
            Commands.Add<PurchaseCommand>("buy");
            Commands.Add<TransactionsCommand>();
            Commands.Add<BalanceCommand>();
            Commands.Add<HelpCommand>("h");
            Commands.Add<LogoutCommand>();
            Commands.Add<UpdatePurseCommand>();
            Commands.Add<UpdateServerCommand>();
            Commands.Add<ChatBatchCommand>();
            Commands.Add<UpdateLocationCommand>();
            Commands.Add<UpdateBitsCommand>();
            Commands.Add<UploadTabCommand>();
            Commands.Add<UploadInventory>();
            Commands.Add<UploadScoreboardCommand>();
            Commands.Add<BackupCommand>();
            Commands.Add<RestoreCommand>();
            Commands.Add<CaptchaCommand>();
            Commands.Add<ImportTfmCommand>();
            Commands.Add<ReplayActiveCommand>();
            Commands.Add<WhichBLEntryCommand>();
            Commands.Add<ReminderCommand>();
            Commands.Add<FiltersCommand>();
            Commands.Add<EmojiCommand>();
            Commands.Add<AddReminderTimeCommand>();
            Commands.Add<LoreCommand>();
            Commands.Add<GlobalMuteCommand>();
            Commands.Add<FactCommand>();
            Commands.Add<TutorialCommand>();
            Commands.Add<FlipCommand>();
            Commands.Add<PreApiCommand>();
            Commands.Add<FoundModsCommand>();
            Commands.Add<ShareItemCommand>();
            Commands.Add<ItemInfoCommand>();
            Commands.Add<SellInventoryCommand>();
            Commands.Add<ClientErrorCommand>();
            Commands.Add<CompensateCommand>();
            Commands.Add<ProxyReqSyncCommand>();
            Commands.Add<TransferCoinsCommand>();
            Commands.Add<LbinCommand>("l");
            Commands.Add<RecentCommand>("r");
            Commands.Add<PingCommand>();
            Commands.Add<SetGuiCommand>();
            Commands.Add<FlipperEstimateCommand>();
            Commands.Add<BazaarCommand>("bz");
            Commands.Add<SwitchRegionCommand>();
            Commands.Add<CopySettingsCommand>();
            Commands.Add<CraftBreakDownCommand>();

            new MinecraftSocket().TryAsyncTimes(async () =>
            {
                NextUpdateStart += () =>
                {
                    var startTime = DateTime.UtcNow;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    var memmoryUsed = GC.GetTotalMemory(true);
                    Console.WriteLine("next update " + (DateTime.UtcNow - startTime) + " memmory used " + memmoryUsed);
                };
                NextFlipTime = DateTime.UtcNow + TimeSpan.FromSeconds(70);
                Timer selfTimer = null!;
                selfTimer = new Timer((t) =>
                {
                    try
                    {
                        NextFlipTime = DateTime.UtcNow + TimeSpan.FromSeconds(70);
                        NextUpdateStart?.Invoke();
                        if (DateTime.UtcNow.Minute % 2 == 0)
                            UpdateTimer();
                        if (selfTimer != tenSecTimer)
                            selfTimer?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        dev.Logger.Instance.Error(ex, "sending next update");
                    }
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
                tenSecTimer = selfTimer;
                var next = await GetNext10SecTime();
                Console.WriteLine($"started timer to start at {next} now its {DateTime.UtcNow}");
            }, "starting timer");
        }

        private static void UpdateTimer()
        {
            Task.Run(async () =>
            {
                using var updateSpan = DiHandler.ServiceProvider.GetRequiredService<ActivitySource>().StartActivity();
                DateTime next = await GetNext10SecTime();
                updateSpan?.SetTag("time", next.ToString(CultureInfo.InvariantCulture));
                tenSecTimer.Change(next - DateTime.UtcNow, TimeSpan.FromMinutes(1));
            }, new CancellationTokenSource(15000).Token);
        }

        private static async Task<DateTime> GetNext10SecTime()
        {
            return (await new NextUpdateRetriever().Get()) - TimeSpan.FromSeconds(12);
        }

        private Activity? CreateActivity(string name, ActivityContext? parent)
        {
            if (parent != null)
                return tracer.StartActivity(name, ActivityKind.Server, parent.Value);
            else
                return tracer.StartActivity(name, ActivityKind.Server);
        }

        public Activity? CreateActivity(string name, Activity? parent = null)
        {
            return CreateActivity(name, parent?.Context);
        }

        protected override void OnOpen()
        {
            ConSpan = CreateActivity("connection") ?? new Activity("connection");
            SendMessage(COFLNET + "§7Established connection, loading settings...",
                        $"https://discord.gg/wvKXfTgCfb",
                        "Attempting to load your settings on " + System.Net.Dns.GetHostName() + " conId: " + ConSpan.Context.TraceId);
            base.OnOpen();
            Task.Run(() =>
            {
                using var openSpan = CreateActivity("open", ConSpan)?.AddTag("clientIp", ClientIp);
                try
                {
                    StartConnection();
                }
                catch (Exception e)
                {
                    Error(e, "starting connection");
                }
            }, new CancellationTokenSource(TimeSpan.FromHours(1)).Token).ConfigureAwait(false);

            Console.CancelKeyPress += OnApplicationStop;

            NextUpdateStart -= SendTimer;
            NextUpdateStart += SendTimer;
        }

        protected override void OnError(ErrorEventArgs e)
        {
            Log(e.Message, Microsoft.Extensions.Logging.LogLevel.Error);
        }

        private void StartConnection()
        {
            NameValueCollection args = SetupModAdapter();

            var passedId = args["player"] ?? args["uuid"];
            TryAsyncTimes(async () => await LoadPlayerName(passedId!), "loading PlayerName");
            Activity.Current?.SetTag("player", passedId);

            (Id, var stringId) = GetService<IdConverter>().ComputeConnectionId(passedId, SessionInfo.clientSessionId);
            ConSpan.SetTag("conId", stringId);

            GetService<FlipperService>().AddNonConnection(this, false);
            SetLifecycleVersion(Version);
            Task.Run(async () =>
            {
                try
                {
                    await sessionLifesycle.SetupConnectionSettings(stringId);
                }
                catch (Exception e)
                {
                    Error(e, "failed to setup connection");
                    SendMessage(new DialogBuilder().CoflCommand<ReportCommand>("Whoops, we are very sorry but the connection setup failed. If this persists please click this message to create a report.", "failed to setup connection", "create a report"));
                }
            }).ConfigureAwait(false);
        }

        protected NameValueCollection SetupModAdapter()
        {
            var args = QueryString;
            if (args["uuid"] == null && args["player"] == null)
                Send(Response.Create("error", "the connection query string needs to include 'player'"));
            if (args["SId"] == null)
            {
                Send(Response.Create("error", "the connection query string needs to include 'SId' with a random string to save login to"));
                SendMessage("§cYou client did not specify a 'SId', your account is vurnable, please update your client to the latest version");
            }
            SessionInfo.clientSessionId = args["SId"].Truncate(60);
            if (args["version"] == null)
            {
                Send(Response.Create("error", "the connection query string needs to include 'version' with client version"));
                // return;
            }
            if (args["type"] != null)
            {
                SessionInfo.ConnectionType = args["type"];
            }
            Version = args["version"].Truncate(14);

            ModAdapter = Version switch
            {
                "1.5.5-Alpha" => new BinGuiVersionAdapter(this),
                "1.5.4-Alpha" => new BinGuiVersionAdapter(this),
                "1.5.2-Alpha" => new BinGuiVersionAdapter(this),
                "1.5.0-Alpha" => new BinGuiVersionAdapter(this),
                "1.4-Alpha" => new InventoryVersionAdapter(this),
                "1.4.2-Alpha" => new InventoryVersionAdapter(this),
                "1.4.3-Alpha" => new InventoryVersionAdapter(this),
                "1.4.4-Alpha" => new InventoryVersionAdapter(this),
                "1.3.3-Alpha" => new ThirdVersionAdapter(this),
                "1.3-Alpha" => new ThirdVersionAdapter(this),
                "1.2-Alpha" => new SecondVersionAdapter(this),
                "1.5.0-af" => new FullAfVersionAdapter(this),
                "1.5.1-af" => new FullAfVersionAdapter(this),
                "1.5.0-afclient" => new AfVersionAdapter(this),
                _ => new FirstModVersionAdapter(this)
            };
            Activity.Current?.SetTag("version", Version);
            return args;
        }

        public void SetLifecycleVersion(string version)
        {
            sessionLifesycle = version switch
            {
                _ => new InventoryModSession(this)
            };
        }

        public System.Runtime.CompilerServices.ConfiguredTaskAwaitable TryAsyncTimes(Func<Task> action, string errorMessage, int times = 3)
        {
            return Task.Run(async () =>
            {
                for (int i = 0; i < times; i++)
                    try
                    {
                        await action().ConfigureAwait(false);
                        return;
                    }
                    catch (Exception e)
                    {
                        Error(e, errorMessage);
                    }
            }, new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token).ConfigureAwait(false);
        }

        public void RecheckVerifiedAccounts()
        {
            _ = TryAsyncTimes(async () =>
            {
                foreach (var item in AccountInfo.McIds.Where(id => !id.Equals(SessionInfo.McUuid)))
                {
                    var connectedCheck = await GetService<McAccountService>().ConnectAccount(UserId, item);
                    if (connectedCheck.IsConnected)
                        continue;
                    AccountInfo.McIds.Remove(item);
                    await sessionLifesycle.AccountInfo.Update(AccountInfo);
                    Dialog(db => db.MsgLine($"Removed an invalid account from your connected accounts. We are sorry for the inconvenience caused by us working hard to find abusive users"));
                }
            }, "rechecking verification");
        }

        public async Task<string> GetPlayerName(string uuid)
        {
            return await DiHandler.GetService<PlayerName.PlayerNameService>()
                    .GetName(uuid) ?? "unknown";
        }
        public async Task<string> GetPlayerUuid(string name, bool blockError = false)
        {
            try
            {
                return await DiHandler.GetService<PlayerName.PlayerNameService>()
                        .GetUuid(name);
            }
            catch (Exception e)
            {
                Error(e, $"loading uuid for name '{name}'");
                if (!blockError)
                    throw new CoflnetException("name_retrieve", "Could not find the player " + name);
                return String.Empty;
            }
        }

        /// <summary>
        /// Gets a service from DI
        /// </summary>
        /// <typeparam name="T">The type to get</typeparam>
        /// <returns></returns>
        public virtual T GetService<T>() where T : class
        {
            return DiHandler.ServiceProvider.GetRequiredService<T>();
        }

        private async Task LoadPlayerName(string passedId)
        {
            using var loadSpan = CreateActivity("loadPlayerName", ConSpan);
            SessionInfo.McName = passedId;
            var uuid = passedId;
            if (passedId.Length >= 32)
                SessionInfo.McName = await GetPlayerName(passedId);
            else
                uuid = await GetPlayerUuid(passedId, true);
            if (SessionInfo.McName == null || uuid == null)
            {
                loadSpan?.SetTag("loading", "externally");
                var profile = await PlayerSearch.Instance.GetMcProfile(passedId) 
                    ?? throw new CoflnetException("player_not_found", $"Mojang doesn't seem to know a player with the name `{passedId}`. Please check for typos and reconnect.");
                uuid = profile.Id;
                SessionInfo.McName = profile.Name;
                await IndexerClient.TriggerNameUpdate(uuid);
            }
            SessionInfo.McUuid = uuid;
            loadSpan?.SetTag("playerId", passedId);
            loadSpan?.SetTag("uuid", uuid);
        }


        private int waiting = 0;

        protected override void OnMessage(MessageEventArgs e)
        {
            var parallelAllowed = 2;
            if ((AccountInfo?.Tier ?? 0) >= AccountTier.PREMIUM_PLUS)
                parallelAllowed = 6;
            else if ((AccountInfo?.Tier ?? 0) >= AccountTier.STARTER_PREMIUM)
                parallelAllowed = 4;

            if (waiting > parallelAllowed)
            {
                SendMessage(COFLNET + $"You are executing too many commands please wait a bit");
                return;
            }
            try
            {

                var a = JsonConvert.DeserializeObject<Response>(e.Data) ?? throw new ArgumentNullException();
                if (e.Data.Contains("dialog") && e.Data.Contains("nobestflip"))
                {
                    HandleCommand(e, null, a);
                    return;
                }
                using var span = CreateActivity("command", ConSpan);
                HandleCommand(e, span, a);
            }
            catch (Exception ex)
            {
                SendMessage(COFLNET + "Your command could not be executed.\n" +
                    "Make sure you send a valid json string eg '{\"type\":\"chat\",\"data\":\"\\\\\"Testing\\\\\"\"}' where the data is another json string");
                Error(ex, "handling command", e.Data);
            }
        }

        private void HandleCommand(MessageEventArgs e, Activity? span, Response? a)
        {
            if (a?.type == null)
            {
                Send(new Response("error", "the payload has to have the property 'type'"));
                return;
            }
            span?.SetTag("type", a.type);
            span?.SetTag("content", a.data);
            if (SessionInfo.clientSessionId.StartsWith("debug"))
                SendMessage("executed " + a.data, "");

            // tokenLogin is the legacy version of clicked
            if (a.type is "tokenLogin" or "clicked")
            {
                ClickCallback(a);
                return;
            }

            if (!Commands.TryGetValue(a.type.ToLower(), out var command))
            {
                var closest = Commands.Where(c => c.Value is { IsPublic: true }).Select(c => c.Key).MinBy(x => Fastenshtein.Levenshtein.Distance(x.ToLower(), a.type));
                var altCommand = $"/cofl {closest} {a.data.Trim('"')}";
                SendMessage($"{COFLNET}The command '{McColorCodes.ITALIC + a.type + McColorCodes.RESET + McCommand.DEFAULT_COLOR}' is not known. Hover for info\n",
                            altCommand.Trim('"'),
                            $"Did you mean '{McColorCodes.ITALIC + closest + McColorCodes.RESET + McCommand.DEFAULT_COLOR}'?\nClick to execute\n{McColorCodes.WHITE + altCommand}");
                return;
            }

            Task.Run(async () =>
            {
                waiting++;
                if (string.IsNullOrEmpty(SessionInfo?.McUuid))
                    await Task.Delay(1200).ConfigureAwait(false);
                if (e.Data == "nobestflip")
                    await InvokeCommand(a, command!);
                else
                {
                    using var commandSpan = CreateActivity(a.type, span);
                    await InvokeCommand(a, command!);
                }
            }, new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token).ConfigureAwait(false);
        }

        private async Task InvokeCommand(Response a, McCommand command)
        {
            try
            {
                await command.Execute(this, a.data).ConfigureAwait(false);
            }
            catch (CoflnetException e)
            {
                Error(e, "mod command coflnet");
                SendMessage(COFLNET + $"{McColorCodes.RED}{e.Message}");
            }
            catch (Exception ex)
            {
                var id = Error(ex, "mod command");
                SendMessage(COFLNET + $"An error occured while processing your command. The error was recorded and will be investigated soon. You can refer to it by {id}",
                    $"http://{id}", "click to open the id as link (and be able to copy)");
            }
            finally
            {
                waiting--;
            }
        }

        private void ClickCallback(Response? a)
        {
            if (!a?.data?.Contains("/viewauction ") ?? true)
                return;
            TryAsyncTimes(async () =>
            {
                var auctionUuid = JsonConvert.DeserializeObject<string>(a.data)?.Trim('"').Replace("/viewauction ", "");
                var flip = LastSent.Where(f => f.Auction.Uuid == auctionUuid).FirstOrDefault();
                if (flip != null && flip.Auction.Context != null && !flip.AdditionalProps.ContainsKey("clickT"))
                    flip.AdditionalProps["clickT"] = (DateTime.UtcNow - flip.Auction.FindTime).ToString();
                await GetService<FlipTrackingService>().ClickFlip(auctionUuid, SessionInfo.McUuid);

            }, "click callback", 1);
        }

        protected override void OnClose(CloseEventArgs? e)
        {
            base.OnClose(e);
            GetService<FlipperService>().RemoveConnection(this);
            ConSpan.SetTag("close reason", e?.Reason);

            ConSpan?.Dispose();
            OnConClose?.Invoke();
            sessionLifesycle?.Dispose();
            TopBlocked.Clear();
        }

        public new void Close()
        {
            ConSpan.SetTag("force close", true);
            base.Close();
        }

        public void SendMessage(string text, string? clickAction = null, string? hoverText = null)
        {
            if (this.ReadyState != WebSocketState.Open)
            {
                RemoveMySelf();
                return;
            }
            try
            {
                Send(Response.Create("writeToChat", new { text, onClick = clickAction, hover = hoverText }));
            }
            catch (Exception e)
            {
                CloseBecauseError(e);
            }
        }

        public void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
        {
            SendMessage(creation.Invoke(new SocketDialogBuilder(this)));
        }


        /// <summary>
        /// Execute a command on the client
        /// use with CAUTION
        /// </summary>
        /// <param name="command">The command to execute</param>
        public void ExecuteCommand(string command)
        {
            Send(Response.Create("execute", command));
        }

        public Activity? RemoveMySelf()
        {
            var span = CreateActivity("removing", ConSpan);
            GetService<FlipperService>().RemoveConnection(this);
            sessionLifesycle.Dispose();
            Task.Run(async () =>
            {
                await Task.Delay(1000).ConfigureAwait(false);
                span?.Dispose();
            });
            return span;
        }

        public bool SendMessage(params ChatPart[] parts)
        {
            if (this.ReadyState != WebSocketState.Open && this.ReadyState != WebSocketState.Connecting)
            {
                RemoveMySelf();
                return false;
            }
            try
            {
                ModAdapter.SendMessage(parts);
                return true;
            }
            catch (Exception e)
            {
                CloseBecauseError(e);
                return false;
            }
        }

        public void SendSound(string soundId, float pitch = 1f)
        {
            ModAdapter.SendSound(soundId, pitch);
        }

        private void CloseBecauseError(Exception e)
        {
            dev.Logger.Instance.Log("removing connection because ");
            dev.Logger.Instance.Error(e);
            using var span = CreateActivity("error", ConSpan)?.AddTag("message", e.Message).AddTag("error", "true");
            span?.Dispose();
            Close();
            sessionLifesycle.Dispose();
            Console.CancelKeyPress -= OnApplicationStop;
        }

        private void OnApplicationStop(object? sender, ConsoleCancelEventArgs e)
        {
            SendMessage(COFLNET + "Server is restarting, you may experience connection issues for a few seconds.",
                 "/cofl start", "if it doesn't auto reconnect click this");
        }

        public virtual string Error(Exception exception, string? message = null, string? additionalLog = null)
        {
            using var error = CreateActivity("error", ConSpan)?.AddTag("message", message).AddTag("error", "true");
            if (IsDevMode || SessionInfo.McUuid == "384a029294fc445e863f2c42fe9709cb")
                dev.Logger.Instance.Error(exception, message);

            error?.AddEvent(new ActivityEvent(message ?? "error", DateTimeOffset.Now, new(new Dictionary<string, object?> {
                { "exception", exception },
                { "additionalLog", additionalLog },
                { "session", JsonConvert.SerializeObject(SessionInfo) },
                { "account", sessionLifesycle.AccountInfo?.Value },
                { "settings", JsonConvert.SerializeObject(Settings).Truncate(10_000) }})));
            return error?.Context.TraceId.ToString()!;
        }

        /// <summary>
        /// Log a message to the connection
        /// </summary>
        /// <param name="message"></param>
        /// <param name="level"></param>
        public new void Log(string message, Microsoft.Extensions.Logging.LogLevel level = Microsoft.Extensions.Logging.LogLevel.Information)
        {
            if (level == Microsoft.Extensions.Logging.LogLevel.Error)
            {
                using var error = CreateActivity("error", ConSpan)?.AddTag("message", message).AddTag("error", "true");
            }
            ConSpan?.AddEvent(new ActivityEvent("message", DateTimeOffset.Now, new(new KeyValuePair<string, object?>[] { new("message", message) })));
        }

        public void Send(Response response)
        {
            if (this.ReadyState == WebSocketState.Closed)
            {
                RemoveMySelf();
                return;
            }
            var json = JsonConvert.SerializeObject(response);
            base.Send(json);
        }

        public async Task<bool> SendFlip(LowPricedAuction flip)
        {
            try
            {
                if (this.ReadyState != WebSocketState.Open)
                {
                    Log("con check was false");
                    return false;
                }
                var start = DateTime.UtcNow;
                await sessionLifesycle.SendFlipBatch(new LowPricedAuction[] { flip });
                var took = DateTime.UtcNow - start;
            }
            catch (Exception e)
            {
                Error(e, "sending flip", JsonConvert.SerializeObject(flip));
                return false;
            }
            return true;
        }


        public string GetFlipMsg(FlipInstance flip)
        {
            return formatProvider.FormatFlip(flip);
        }


        public string FormatPrice(double price)
        {
            if (Settings?.ModSettings?.ShortNumbers ?? false)
                return FormatProvider.FormatPriceShort(price);
            return string.Format("{0:n0}", price);
        }


        /// <summary>
        /// Tell the client that a flip isn't available anymore
        /// </summary>
        /// <param name="uuid"></param>
        /// <returns></returns>
        public Task<bool> SendSold(string uuid)
        {
            if (this.ReadyState != WebSocketState.Open)
                return Task.FromResult(false);
            // don't send extra messages
            return Task.FromResult(true);
        }


        private void SendTimer()
        {
            using var timer = CreateActivity("timer", ConSpan);
            if (this.ReadyState == WebSocketState.Closed)
            {
                NextUpdateStart -= SendTimer;
                return;
            }
            sessionLifesycle.HouseKeeping();
            if (HasFlippingDisabled())
            {
                // ping is sent to keep the connection open (after 60 seconds inactivity its disconnected by cloudflare)
                Send(Response.Create("ping", 0));
                timer.Log("flips disabled");
                _ = TryAsyncTimes(() => this.TriggerTutorial<ModCommands.Tutorials.FlipToggling>(), "sending flip tutorial");
                return;
            }

            if(ahActive.IsAhDisabledDerpy)
            {
                Send(Response.Create("ping", 0));
                timer.Log("ah disabled");
                return;
            }

            if (Settings?.ModSettings?.DisplayTimer ?? false)
            {
                var mod = Settings.ModSettings;
                if (mod.TimerSeconds == 0)
                    sessionLifesycle.StartTimer(10 - SessionInfo.RelativeSpeed.TotalSeconds);
                else
                {
                    ScheduleTimer(mod, timer);
                }
            }
            else
                timer.Log("timer disabled");
            if (!(Settings?.ModSettings?.BlockTenSecondsMsg ?? true))
            {
                SendMessage(
                            COFLNET + "Flips in 10 seconds",
                            null,
                            $"The Hypixel API will update in about 10 seconds. Get ready to receive the latest flips. \n"
                            + "(this is an automated message being sent 50 seconds after the last update)\n"
                            + $"you can toggle this message with {McColorCodes.AQUA}/cofl set modblockTenSecMsg");

                if (Settings?.ModSettings?.PlaySoundOnFlip ?? false)
                    SendSound("note.hat", 1);
            }
        }

        public bool HasFlippingDisabled()
        {
            return !SessionInfo.IsMacroBot && (Settings?.DisableFlips ?? false) || !SessionInfo.FlipsEnabled && Settings != null || SessionInfo.IsNotFlipable || (Settings?.ModSettings?.AhDataOnlyMode ?? false);
        }

        public void ScheduleTimer(ModSettings? mod = null, Activity? timerSpan = null)
        {
            if (mod == null)
                mod = Settings.ModSettings;

            var timerSeconds = mod.TimerSeconds == 0 ? 10 : mod.TimerSeconds;

            var nextUpdateIn = NextFlipTime - DateTime.UtcNow;
            var countdownSize = TimeSpan.FromSeconds(timerSeconds);
            if (nextUpdateIn < countdownSize)
            {
                sessionLifesycle.StartTimer(nextUpdateIn.TotalSeconds);
                timerSpan?.Log("scheduled timer to " + nextUpdateIn.TotalSeconds + " seconds");
                return;
            }
            var delay = nextUpdateIn - countdownSize - SessionInfo.RelativeSpeed;
            timerSpan?.Log($"delaying timer for {delay.TotalSeconds} seconds");
            TryAsyncTimes(async () =>
            {
                await Task.Delay(delay).ConfigureAwait(false);
                sessionLifesycle.StartTimer(timerSeconds);
            }, "scheduling timer", 1);
        }

        private static string GetEnableMessage(object newSettings, System.Reflection.FieldInfo prop)
        {
            if (prop.GetValue(newSettings)?.Equals(true) ?? false)
                return prop.Name + " got enabled";
            return prop.Name + " was disabled";
        }

        public LowPricedAuction GetFlip(string uuid)
        {
            return LastSent.Concat(TopBlocked.Select(b => b.Flip)).Where(s => s.Auction.Uuid == uuid).FirstOrDefault();
        }

        public async Task<bool> SendFlip(FlipInstance flip)
        {
            var props = flip.Context;
            if (props == null)
                props = new Dictionary<string, string>();
            if (flip.Sold)
                props["sold"] = "y";
            var result = await SendFlip(new LowPricedAuction()
            {
                Auction = flip.Auction,
                DailyVolume = flip.Volume,
                Finder = flip.Finder,
                TargetPrice = flip.MedianPrice,
                AdditionalProps = props
            }).ConfigureAwait(false);
            if (!result)
            {
                Log("failed");
                Log(this.ReadyState.ToString());
            }


            return true;
        }

        public async Task<AccountTier> UserAccountTier()
        {
            var tier = sessionLifesycle.AccountInfo?.Value?.Tier;
            var expiresAt = sessionLifesycle.AccountInfo?.Value?.ExpiresAt;
            if (tier >= AccountTier.NONE && expiresAt < DateTime.UtcNow + TimeSpan.FromMinutes(30) && expiresAt > DateTime.UtcNow - TimeSpan.FromHours(1))
            {
                // refresh tier
                tier = await sessionLifesycle.UpdateAccountTier(sessionLifesycle.AccountInfo?.Value);
            }
            else if (tier == null || expiresAt < DateTime.UtcNow)
                tier = AccountTier.NONE;
            return tier.Value;
        }

        public Task SendBatch(IEnumerable<LowPricedAuction> flips)
        {
            return sessionLifesycle.SendFlipBatch(flips);
        }

        public override bool Equals(object? obj)
        {
            return obj is MinecraftSocket socket &&
                   ID == socket.ID &&
                   StartTime == socket.StartTime &&
                   Id == socket.Id;
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(ID);
            hash.Add(StartTime);
            return hash.ToHashCode();
        }
    }
#nullable restore
}
