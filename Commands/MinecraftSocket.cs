using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Coflnet.Sky.Commands.Helper;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using hypixel;
using Jaeger.Samplers;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Commands.MC
{
    public partial class MinecraftSocket : WebSocketBehavior, IFlipConnection
    {
        public string McId;
        public string McUuid = "00000000000000000";
        public static string COFLNET = "[§1C§6oflnet§f]§7: ";

        public long Id { get; private set; }

        public SessionInfo sessionInfo { get; protected set; } = new SessionInfo();

        public FlipSettings Settings => LatestSettings.Settings;
        public int UserId => LatestSettings.UserId;
        public SettingsChange LatestSettings { get; set; } = new SettingsChange() { Settings = DEFAULT_SETTINGS };

        public string Version { get; private set; }
        public OpenTracing.ITracer tracer = new Jaeger.Tracer.Builder("sky-commands-mod").WithSampler(new ConstSampler(true)).Build();
        public OpenTracing.ISpan ConSpan { get; private set; }
        private System.Threading.Timer PingTimer;

        public IModVersionAdapter ModAdapter;

        public static FlipSettings DEFAULT_SETTINGS = new FlipSettings() { MinProfit = 100000, MinVolume = 20, 
            ModSettings = new ModSettings(), 
            Visibility = new VisibilitySettings() {SellerOpenButton = true, ExtraInfoMax = 3} };

        public static ClassNameDictonary<McCommand> Commands = new ClassNameDictonary<McCommand>();

        public static event Action NextUpdateStart;
        private int blockedFlipFilterCount => TopBlocked.Count;

        private static System.Threading.Timer updateTimer;

        private ConcurrentDictionary<long, DateTime> SentFlips = new ConcurrentDictionary<long, DateTime>();
        public ConcurrentQueue<BlockedElement> TopBlocked = new ConcurrentQueue<BlockedElement>();
        public ConcurrentQueue<LowPricedAuction> LastSent = new ConcurrentQueue<LowPricedAuction>();

        public class BlockedElement
        {
            public LowPricedAuction Flip;
            public string Reason;
        }
        private static Prometheus.Counter sentFlipsCount = Prometheus.Metrics.CreateCounter("sky_commands_sent_flips", "How many flip messages were sent");

        static MinecraftSocket()
        {
            Commands.Add<TestCommand>();
            Commands.Add<SoundCommand>();
            Commands.Add<ReferenceCommand>();
            Commands.Add<ReportCommand>();
            Commands.Add<PurchaseStartCommand>();
            Commands.Add<PurchaseConfirmCommand>();
            Commands.Add<ClickedCommand>();
            Commands.Add<ResetCommand>();
            Commands.Add<OnlineCommand>();
            Commands.Add<BlacklistCommand>();
            Commands.Add<FastCommand>();
            Commands.Add<VoidCommand>();
            Commands.Add<BlockedCommand>();
            Commands.Add<ChatCommand>();
            Commands.Add<CCommand>();
            Commands.Add<RateCommand>();
            Commands.Add<TimeCommand>();
            Commands.Add<DialogCommand>();
            Commands.Add<ProfitCommand>();
            Commands.Add<AhOpenCommand>();
            Commands.Add<SetCommand>();

            Task.Run(async () =>
            {
                var next = await new NextUpdateRetriever().Get();

                NextUpdateStart += () =>
                {
                    Console.WriteLine("next update");
                    GC.Collect();
                };
                while (next < DateTime.Now)
                    next += TimeSpan.FromMinutes(1);
                Console.WriteLine($"started timer to start at {next} now its {DateTime.Now}");
                updateTimer = new System.Threading.Timer((e) =>
                {
                    try
                    {
                        NextUpdateStart?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        dev.Logger.Instance.Error(ex, "sending next update");
                    }
                }, null, next - DateTime.Now, TimeSpan.FromMinutes(1));
            }).ConfigureAwait(false);
        }

        protected override void OnOpen()
        {
            ConSpan = tracer.BuildSpan("connection").Start();
            base.OnOpen();
            Task.Run(() =>
            {
                using var openSpan = tracer.BuildSpan("open").AsChildOf(ConSpan).StartActive();
                try
                {
                    StartConnection(openSpan);
                }
                catch (Exception e)
                {
                    Error(e, "starting connection");
                }
            }).ConfigureAwait(false);

        }

        private void StartConnection(OpenTracing.IScope openSpan)
        {
            SendMessage(COFLNET + "§fNOTE §7This is a development preview, it is NOT stable/bugfree", $"https://discord.gg/wvKXfTgCfb", System.Net.Dns.GetHostName());
            var args = System.Web.HttpUtility.ParseQueryString(Context.RequestUri.Query);
            Console.WriteLine(Context.RequestUri.Query);
            if (args["uuid"] == null && args["player"] == null)
                Send(Response.Create("error", "the connection query string needs to include 'player'"));
            if (args["SId"] != null)
                sessionInfo.sessionId = args["SId"].Truncate(60);
            if (args["version"] != null)
                Version = args["version"].Truncate(10);

            ModAdapter = Version switch
            {
                "1.3-Alpha" => new ThirdVersionAdapter(this),
                "1.2-Alpha" => new SecondVersionAdapter(this),
                _ => new FirstModVersionAdapter(this)
            };

            McId = args["player"] ?? args["uuid"];
            ConSpan.SetTag("uuid", McId);
            ConSpan.SetTag("version", Version);

            string stringId;
            (this.Id, stringId) = ComputeConnectionId();
            ConSpan.SetTag("conId", stringId);


            if (Settings == null)
                LatestSettings.Settings = DEFAULT_SETTINGS;
            FlipperService.Instance.AddNonConnection(this, false);
            System.Threading.Tasks.Task.Run(async () =>
            {
                await SetupConnectionSettings(stringId);
            }).ConfigureAwait(false);

            PingTimer = new System.Threading.Timer((e) =>
            {
                SendPing();
            }, null, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(50));
        }

        private async Task SetupConnectionSettings(string stringId)
        {
            SettingsChange cachedSettings = null;
            for (int i = 0; i < 3; i++)
            {
                cachedSettings = await CacheService.Instance.GetFromRedis<SettingsChange>(this.Id.ToString());
                if(cachedSettings != null)
                    break;
                await Task.Delay(800); // backoff to give redis time to recover
            }
            
            if (cachedSettings != null)
            {
                try
                {
                    MigrateSettings(cachedSettings);
                    this.LatestSettings = cachedSettings;
                    UpdateConnectionTier(cachedSettings);
                    await SendAuthorizedHello(cachedSettings);
                    // set them again
                    this.LatestSettings = cachedSettings;
                    SendMessage(COFLNET + $"§fFound and loaded settings for your connection\n"
                        + $"{McColorCodes.GRAY} MinProfit: {McColorCodes.AQUA}{FormatPrice(Settings.MinProfit)}  "
                        + $"{McColorCodes.GRAY} MaxCost: {McColorCodes.AQUA}{FormatPrice(Settings.MaxCost)}"
                        + $"{McColorCodes.GRAY} Blacklist-Size: {McColorCodes.AQUA}{Settings?.BlackList?.Count ?? 0}\n "
                        + (Settings.BasedOnLBin ? $"{McColorCodes.RED} Your profit is based on Lowest bin, please note that this is NOT the intended way to use this\n " : "")
                        + $"{McColorCodes.AQUA}: click this if you want to change a setting \n"
                        + "§8: nothing else to do have a nice day :)",
                        "https://sky.coflnet.com/flipper");
                    Console.WriteLine($"loaded settings for {this.sessionInfo.sessionId} " + JsonConvert.SerializeObject(cachedSettings));
                    await Task.Delay(500);
                    SendMessage(COFLNET + $"{McColorCodes.DARK_GREEN} click this to relink your account",
                    GetAuthLink(stringId), "You don't need to relink your account. \nThis is only here to allow you to link your mod to the website again should you notice your settings aren't updated");
                    return;
                }
                catch (Exception e)
                {
                    Error(e, "loading modsocket");
                    SendMessage(COFLNET + $"Your settings could not be loaded, please relink again :)");
                }
            }
            var index = 1;
            while (true)
            {
                SendMessage(COFLNET + "§lPlease click this [LINK] to login and configure your flip filters §8(you won't receive real time flips until you do)",
                    GetAuthLink(stringId));
                await Task.Delay(TimeSpan.FromSeconds(60 * index));

                if (Settings != DEFAULT_SETTINGS)
                    return;
                SendMessage("do /cofl stop to stop receiving this (or click this message)", "/cofl stop");
            }
        }

        private static void MigrateSettings(SettingsChange cachedSettings)
        {
            var currentVersion = 3;
            if (cachedSettings.Version >= currentVersion)
                return;
            if (cachedSettings.Settings.AllowedFinders == LowPricedAuction.FinderType.UNKOWN)
                cachedSettings.Settings.AllowedFinders = LowPricedAuction.FinderType.FLIPPER | LowPricedAuction.FinderType.SNIPER_MEDIAN;
            cachedSettings.Version = currentVersion;
        }

        private string GetAuthLink(string stringId)
        {
            return $"https://sky.coflnet.com/authmod?mcid={McId}&conId={HttpUtility.UrlEncode(stringId)}";
        }

        public async Task<string> GetPlayerName(string uuid)
        {
            return (await Shared.DiHandler.ServiceProvider.GetRequiredService<PlayerName.Client.Api.PlayerNameApi>()
                    .PlayerNameNameUuidGetAsync(uuid))?.Trim('"');
        }

        private async Task SendAuthorizedHello(SettingsChange cachedSettings)
        {
            var player = await PlayerService.Instance.GetPlayer(this.McId);
            var mcName = player?.Name;
            McUuid = player.UuId;
            var user = UserService.Instance.GetUserById(cachedSettings.UserId);
            var length = user.Email.Length < 10 ? 3 : 6;
            var builder = new StringBuilder(user.Email);
            for (int i = 0; i < builder.Length - 5; i++)
            {
                if (builder[i] == '@' || i < 3)
                    continue;
                builder[i] = '*';
            }
            var anonymisedEmail = builder.ToString();
            var messageStart = $"Hello {mcName} ({anonymisedEmail}) \n";
            if (cachedSettings.Tier != AccountTier.NONE && cachedSettings.ExpiresAt > DateTime.Now)
                SendMessage(COFLNET + messageStart + $"You have {cachedSettings.Tier.ToString()} until {cachedSettings.ExpiresAt}");
            else
                SendMessage(COFLNET + messageStart + $"You use the free version of the flip finder");

            await Task.Delay(300);
        }

        private void SendPing()
        {
            using var span = tracer.BuildSpan("ping").AsChildOf(ConSpan.Context).WithTag("count", blockedFlipFilterCount).StartActive();
            try
            {
                if (blockedFlipFilterCount > 0)
                {
                    SendMessage(COFLNET + $"there were {blockedFlipFilterCount} flips blocked by your filter the last minute", null, $"{McColorCodes.GRAY} execute {McColorCodes.AQUA}/cofl blocked{McColorCodes.GRAY} to list blocked flips");
                }
                else
                {
                    Send(Response.Create("ping", 0));

                    UpdateConnectionTier(LatestSettings);
                }
            }
            catch (Exception e)
            {
                span.Span.Log("could not send ping");
                CloseBecauseError(e);
            }
        }

        protected (long, string) ComputeConnectionId()
        {
            var bytes = Encoding.UTF8.GetBytes(McId.ToLower() + sessionInfo.sessionId + DateTime.Now.Date.ToString());
            var hash = System.Security.Cryptography.SHA512.Create();
            var hashed = hash.ComputeHash(bytes);
            return (BitConverter.ToInt64(hashed), Convert.ToBase64String(hashed, 0, 16).Replace('+', '-').Replace('/', '_'));
        }

        int waiting = 0;

        protected override void OnMessage(MessageEventArgs e)
        {
            if (waiting > 2)
            {
                SendMessage(COFLNET + $"You are executing to many commands please wait a bit");
                return;
            }
            using var span = tracer.BuildSpan("Command").AsChildOf(ConSpan.Context).StartActive();

            var a = JsonConvert.DeserializeObject<Response>(e.Data);
            if (a == null || a.type == null)
            {
                Send(new Response("error", "the payload has to have the property 'type'"));
                return;
            }
            span.Span.SetTag("type", a.type);
            span.Span.SetTag("content", a.data);
            if (sessionInfo.sessionId.StartsWith("debug"))
                SendMessage("executed " + a.data, "");

            // tokenlogin is the legacy version of clicked
            if (a.type == "tokenLogin" || a.type == "clicked")
            {
                ClickCallback(a);
                return;
            }

            if (!Commands.TryGetValue(a.type.ToLower(), out McCommand command))
            {
                SendMessage($"The command '{a.type}' is not know. Please check your spelling ;)");
                return;
            }

            Task.Run(async () =>
            {
                waiting++;
                try
                {
                    await command.Execute(this, a.data);
                }
                catch (Exception ex)
                {
                    var id = Error(ex, "mod command");
                    SendMessage(COFLNET + $"An error occured while processing your command. The error was recorded and will be investigated soon. You can refer to it by {id}", "http://" + id, "click to open the id as link (and be able to copy)");
                }
                finally
                {
                    waiting--;
                }
            });
        }

        private void ClickCallback(Response a)
        {
            if (a.data.Contains("/viewauction "))
                Task.Run(async () =>
                {
                    var auctionUuid = a.data.Trim('"').Replace("/viewauction ", "");
                    var flip = LastSent.Where(f => f.Auction.Uuid == auctionUuid).FirstOrDefault();
                    if (flip != null && flip.Auction.Context != null)
                        flip.AdditionalProps["clickT"] = (DateTime.Now - flip.Auction.FindTime).ToString();
                    await FlipTrackingService.Instance.ClickFlip(auctionUuid, McUuid);

                });
        }

        protected override void OnClose(CloseEventArgs e)
        {
            base.OnClose(e);
            FlipperService.Instance.RemoveConnection(this);
            ConSpan.Log(e?.Reason);

            ConSpan.Finish();
        }

        public void SendMessage(string text, string clickAction = null, string hoverText = null)
        {
            if (ConnectionState != WebSocketState.Open)
            {
                OpenTracing.IScope span = RemoveMySelf();
                return;
            }
            try
            {
                this.Send(Response.Create("writeToChat", new { text, onClick = clickAction, hover = hoverText }));
            }
            catch (Exception e)
            {
                CloseBecauseError(e);
            }
        }

        /// <summary>
        /// Execute a command on the client
        /// use with CAUTION
        /// </summary>
        /// <param name="command">The command to execute</param>
        public void ExecuteCommand(string command)
        {
            this.Send(Response.Create("execute", command));
        }

        private OpenTracing.IScope RemoveMySelf()
        {
            var span = tracer.BuildSpan("removing").AsChildOf(ConSpan).StartActive();
            FlipperService.Instance.RemoveConnection(this);
            PingTimer.Dispose();
            return span;
        }

        public bool SendMessage(params ChatPart[] parts)
        {
            if (ConnectionState != WebSocketState.Open)
            {
                RemoveMySelf();
                return false;
            }
            try
            {
                this.ModAdapter.SendMessage(parts);
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

        private OpenTracing.IScope CloseBecauseError(Exception e)
        {
            dev.Logger.Instance.Log("removing connection because " + e.Message);
            dev.Logger.Instance.Error(System.Environment.StackTrace);
            var span = tracer.BuildSpan("Disconnect").WithTag("error", "true").AsChildOf(ConSpan.Context).StartActive();
            span.Span.Log(e.Message);
            OnClose(null);
            PingTimer.Dispose();
            return span;
        }

        private string Error(Exception exception, string message = null, string additionalLog = null)
        {
            using var error = tracer.BuildSpan("error").WithTag("message", message).WithTag("error", "true").StartActive();
            if (System.Net.Dns.GetHostName().Contains("ekwav"))
                Console.WriteLine(exception.Message + "\n" + exception.StackTrace);
            AddExceptionLog(error, exception);
            if (additionalLog != null)
                error.Span.Log(additionalLog);

            return error.Span.Context.TraceId;
        }

        private void AddExceptionLog(OpenTracing.IScope error, Exception e)
        {
            error.Span.Log(e.Message);
            error.Span.Log(e.StackTrace);
            if (e.InnerException != null)
                AddExceptionLog(error, e.InnerException);
        }

        public void Send(Response response)
        {
            var json = JsonConvert.SerializeObject(response);
            this.Send(json);
        }

        public async Task<bool> SendFlip(LowPricedAuction flip)
        {
            try
            {
                if (base.ConnectionState != WebSocketState.Open)
                    return false;
                // pre check already sent flips
                if (SentFlips.ContainsKey(flip.UId))
                    return true; // don't double send

                if (!flip.Auction.Bin) // no nonbin 
                    return true;

                if (flip.AdditionalProps?.ContainsKey("sold") ?? false)
                {
                    BlockedFlip(flip, "sold");
                    return true;
                }
                var flipInstance = FlipperService.LowPriceToFlip(flip);
                // fast match before fill
                Settings.GetPrice(flipInstance, out _, out long profit);
                if (!Settings.BasedOnLBin && Settings.MinProfit > profit)
                    return BlockedFlip(flip, "MinProfit");
                var isMatch = (false, "");
                if (!Settings.FastMode)
                    await FlipperService.FillVisibilityProbs(flipInstance, this.Settings);
                try
                {
                    isMatch = Settings.MatchesSettings(flipInstance);
                    if (flip.AdditionalProps == null)
                        flip.AdditionalProps = new Dictionary<string, string>();
                    flip.AdditionalProps["match"] = isMatch.Item2;
                }
                catch (Exception e)
                {
                    var id = Error(e, "matching flip settings", JSON.Stringify(flip) + "\n" + JSON.Stringify(Settings));
                    dev.Logger.Instance.Error(e, "minecraft socket flip settings matching " + id);
                    BlockedFlip(flip, "Error " + e.Message);
                }
                if (Settings != null && !isMatch.Item1)
                {
                    BlockedFlip(flip, isMatch.Item2);
                    return true;
                }

                // this check is down here to avoid filling up the list
                if (!SentFlips.TryAdd(flip.UId, DateTime.Now))
                    return true; // make sure flips are not sent twice
                using var span = tracer.BuildSpan("Flip").WithTag("uuid", flipInstance.Uuid).AsChildOf(ConSpan.Context).StartActive();
                var settings = Settings;

                if (base.ConnectionState != WebSocketState.Open)
                {
                    RemoveMySelf();
                    return false;
                }
                await ModAdapter.SendFlip(flipInstance);

                flip.AdditionalProps["csend"] = (DateTime.Now - flipInstance.Auction.FindTime).ToString();

                span.Span.Log("sent");
                LastSent.Enqueue(flip);
                sentFlipsCount.Inc();

                PingTimer.Change(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(55));

                var track = Task.Run(async () =>
                {
                    await FlipTrackingService.Instance.ReceiveFlip(flip.Auction.Uuid, McUuid);
                    // remove dupplicates
                    if (SentFlips.Count > 300)
                    {
                        foreach (var item in SentFlips.Where(i => i.Value < DateTime.Now - TimeSpan.FromMinutes(2)).ToList())
                        {
                            SentFlips.TryRemove(item.Key, out DateTime value);
                        }
                    }
                    if (LastSent.Count > 30)
                        LastSent.TryDequeue(out _);
                });
            }
            catch (Exception e)
            {
                Error(e, "sending flip");
                return false;
            }
            return true;
        }

        private bool BlockedFlip(LowPricedAuction flip, string reason)
        {

            TopBlocked.Enqueue(new BlockedElement()
            {
                Flip = flip,
                Reason = reason
            });
            return true;
        }

        public string GetFlipMsg(FlipInstance flip)
        {
            Settings.GetPrice(flip, out long targetPrice, out long profit);
            var priceColor = GetProfitColor((int)profit);
            var finderType = flip.Finder.HasFlag(LowPricedAuction.FinderType.SNIPER) ? "SNIPE" : "FLIP";
            var a = flip.Auction;
            if (Settings.ModSettings.Format != null)
            {
                /*
                    "\n{0}: {1}{2} {3}{4} -> {5} (+{6} {7}) Med: {8} Lbin: {9} Volume: {10}"
                    {0} FlipFinder
                    {1} Item Rarity Color
                    {2} Item Name
                    {3} Price color
                    {4} Starting bid
                    {5} Target Price
                    {6} Estimated Profit
                    {7} Provit percentage
                    {8} Median Price
                    {9} Lowest Bin
                    {10}Volume
                */
                return String.Format(Settings.ModSettings.Format,
                    finderType,
                    GetRarityColor(a.Tier),
                    a.ItemName,
                    priceColor,
                    FormatPrice(a.StartingBid),
                    FormatPrice(targetPrice), // this is {5}
                    FormatPrice(profit),
                    FormatPrice((profit * 100 / a.StartingBid)),
                    FormatPrice(flip.MedianPrice),
                    FormatPrice(flip.LowestBin ?? 0),
                    flip.Volume  // this is {10}
                );
            }
            var textAfterProfit = (Settings?.Visibility?.ProfitPercentage ?? false) ? $" {McColorCodes.DARK_RED}{FormatPrice((profit * 100 / a.StartingBid))}%{priceColor}" : "";

            var builder = new StringBuilder(80);

            builder.Append($"\n{finderType}: {GetRarityColor(a.Tier)}{a.ItemName} {priceColor}{FormatPrice(a.StartingBid)} -> {FormatPrice(targetPrice)} ");
            if ((Settings.Visibility?.Profit ?? false) || (Settings.Visibility?.EstimatedProfit ?? false))
                builder.Append($"(+{FormatPrice(profit)}{textAfterProfit}) ");
            if (Settings.Visibility?.MedianPrice ?? false)
                builder.Append(McColorCodes.GRAY + " Med: " + McColorCodes.AQUA + FormatPrice(flip.MedianPrice));
            if (Settings.Visibility?.LowestBin ?? false)
                builder.Append(McColorCodes.GRAY + " LBin: " + McColorCodes.AQUA + FormatPrice(flip.LowestBin ?? 0));
            if (Settings.Visibility?.Volume ?? false)
                builder.Append(McColorCodes.GRAY + " Vol: " + McColorCodes.AQUA + flip.Volume.ToString("0.#"));
            return builder.ToString();
        }

        public string GetHoverText(FlipInstance flip)
        {
            if(Settings.Visibility.Lore)
                return flip.Auction.Context.GetValueOrDefault("lore");
            return string.Join('\n', flip.Interesting.Select(s => "・" + s)) + "\n" + flip.SellerName;
        }

        public string GetRarityColor(Tier rarity)
        {
            return rarity switch
            {
                Tier.COMMON => "§f",
                Tier.EPIC => "§5",
                Tier.UNCOMMON => "§a",
                Tier.RARE => "§9",
                Tier.SPECIAL => "§c",
                Tier.SUPREME => "§4",
                Tier.VERY_SPECIAL => "§4",
                Tier.LEGENDARY => "§6",
                Tier.MYTHIC => "§d",
                _ => ""
            };
        }

        public string GetProfitColor(int profit)
        {
            if (profit >= 50_000_000)
                return McColorCodes.GOLD;
            if (profit >= 10_000_000)
                return McColorCodes.AQUA;
            if (profit >= 1_000_000)
                return McColorCodes.GREEN;
            if (profit >= 100_000)
                return McColorCodes.DARK_GREEN;
            return McColorCodes.DARK_GRAY;
        }

        public string FormatPrice(long price)
        {
            if (Settings.ModSettings?.ShortNumbers ?? false)
                return FormatPriceShort(price);
            return string.Format("{0:n0}", price);
        }

        /// <summary>
        /// By RenniePet on Stackoverflow
        /// https://stackoverflow.com/a/30181106
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        private static string FormatPriceShort(long num)
        {
            if (num <= 0) // there was an issue with flips attempting to be devided by 0
                return "0";
            // Ensure number has max 3 significant digits (no rounding up can happen)
            long i = (long)Math.Pow(10, (int)Math.Max(0, Math.Log10(num) - 2));
            num = num / i * i;

            if (num >= 1000000000)
                return (num / 1000000000D).ToString("0.##") + "B";
            if (num >= 1000000)
                return (num / 1000000D).ToString("0.##") + "M";
            if (num >= 1000)
                return (num / 1000D).ToString("0.##") + "k";

            return num.ToString("#,0");
        }

        public Task<bool> SendSold(string uuid)
        {
            if (base.ConnectionState != WebSocketState.Open)
                return Task.FromResult(false);
            // don't send extra messages
            return Task.FromResult(true);
        }

        public void UpdateSettings(SettingsChange settings)
        {
            var settingsSame = AreSettingsTheSame(settings);
            using var span = tracer.BuildSpan("SettingsUpdate").AsChildOf(ConSpan.Context)
                    .WithTag("premium", settings.Tier.ToString())
                    .WithTag("userId", settings.UserId.ToString())
                    .StartActive();
            if (this.Settings == DEFAULT_SETTINGS)
            {
                Task.Run(async () => await ModGotAuthorised(settings));
            }
            else if (!settingsSame)
            {
                var changed = FindWhatsNew(this.Settings, settings.Settings);
                SendMessage($"{COFLNET} setting changed " + changed);
                span.Span.Log(changed);
            }
            LatestSettings = settings;
            UpdateConnectionTier(settings);

            CacheService.Instance.SaveInRedis(this.Id.ToString(), settings, TimeSpan.FromDays(3))
            .Wait(); // this call is synchronised because redis is set to fire and forget (returns instantly)
            span.Span.Log(JSON.Stringify(settings));
        }

        public Task UpdateSettings(Func<SettingsChange, SettingsChange> updatingFunc)
        {
            var newSettings = updatingFunc(this.LatestSettings);
            return FlipperService.Instance.UpdateSettings(newSettings);
        }

        private async Task<OpenTracing.IScope> ModGotAuthorised(SettingsChange settings)
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
                Error(e, "settings authorization");
                span.Span.Log(e.Message);
            }

            //await Task.Delay(TimeSpan.FromMinutes(2));
            try
            {
                await CheckVerificationStatus(settings);
            }
            catch (Exception e)
            {
                Error(e, "verification failed");
            }

            return span;
        }

        private async Task CheckVerificationStatus(SettingsChange settings)
        {
            var connect = await McAccountService.Instance.ConnectAccount(settings.UserId.ToString(), McUuid);
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

            SendMessage(new ChatPart(
                $"{COFLNET}You connected from an unkown account. Please verify that you are indeed {McId} by bidding {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on a random auction.",
                $"/viewauction {targetAuction?.Uuid}",
                $"{McColorCodes.GRAY}Click to open an auction to bid {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on\nyou can also bid another number with the same digits at the end\neg. 1,234,{McColorCodes.AQUA}{bid}"));

        }

        /// <summary>
        /// Tests if the given settings are different from the current active ones
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        private bool AreSettingsTheSame(SettingsChange settings)
        {
            return MessagePack.MessagePackSerializer.Serialize(settings.Settings).SequenceEqual(MessagePack.MessagePackSerializer.Serialize(Settings));
        }

        private void UpdateConnectionTier(SettingsChange settings)
        {
            if ((settings.Tier.HasFlag(AccountTier.PREMIUM) || settings.Tier.HasFlag(AccountTier.STARTER_PREMIUM)) && settings.ExpiresAt > DateTime.Now)
            {
                FlipperService.Instance.AddConnection(this, false);
                NextUpdateStart -= SendTimer;
                NextUpdateStart += SendTimer;
            }
            else if (settings.Tier == AccountTier.PREMIUM_PLUS)
                FlipperService.Instance.AddConnectionPlus(this, false);
            else
                FlipperService.Instance.AddNonConnection(this, false);
            this.ConSpan.SetTag("tier", settings.Tier.ToString());
        }

        private void SendTimer()
        {
            if (base.ConnectionState != WebSocketState.Open)
            {
                NextUpdateStart -= SendTimer;
                return;
            }
            if (Settings?.ModSettings?.BlockTenSecondsMsg ?? false)
            {
                Send(Response.Create("ping", 0));
                return;
            }
            SendMessage(
                COFLNET + "Flips in 10 seconds",
                null,
                "The Hypixel API will update in 10 seconds. Get ready to receive the latest flips. "
                + "(this is an automated message being sent 50 seconds after the last update)");
            TopBlocked.Clear();
            if (Settings?.ModSettings?.PlaySoundOnFlip  ?? false)
                SendSound("note.hat", 1);
        }

        public string FindWhatsNew(FlipSettings current, FlipSettings newSettings)
        {
            try
            {
                if (current.MinProfit != newSettings.MinProfit)
                    return "min Profit to " + FormatPrice(newSettings.MinProfit);
                if (current.MinProfit != newSettings.MinProfit)
                    return "max Cost to " + FormatPrice(newSettings.MaxCost);
                if (current.MinProfitPercent != newSettings.MinProfitPercent)
                    return "min profit percentage to " + FormatPrice(newSettings.MinProfitPercent);
                if (current.BlackList?.Count < newSettings.BlackList?.Count)
                    return $"blacklisted item " + ItemDetails.TagToName(newSettings.BlackList?.Last()?.ItemTag);
                if (current.WhiteList?.Count < newSettings.WhiteList?.Count)
                    return $"whitelisted item " + ItemDetails.TagToName(newSettings.BlackList?.Last()?.ItemTag);
                if (current.Visibility != null)
                    foreach (var prop in current.Visibility?.GetType().GetFields())
                    {
                        if (prop.GetValue(current.Visibility).ToString() != prop.GetValue(newSettings.Visibility).ToString())
                        {
                            return GetEnableMessage(newSettings.Visibility, prop);
                        }
                    }
                if (current.ModSettings != null)
                    foreach (var prop in current.ModSettings?.GetType().GetFields())
                    {
                        if (prop.GetValue(current.ModSettings)?.ToString() != prop.GetValue(newSettings.ModSettings)?.ToString())
                        {
                            return GetEnableMessage(newSettings.ModSettings, prop);
                        }
                    }
            }
            catch (Exception e)
            {
                Error(e, "updating settings");
            }

            return "";
        }

        private static string GetEnableMessage(object newSettings, System.Reflection.FieldInfo prop)
        {
            if (prop.GetValue(newSettings).Equals(true))
                return prop.Name + " got enabled";
            return prop.Name + " was disabled";
        }

        public LowPricedAuction GetFlip(string uuid)
        {
            return LastSent.Where(s => s.Auction.Uuid == uuid).FirstOrDefault();
        }

        public Task<bool> SendFlip(FlipInstance flip)
        {
            var props = flip.Context;
            if (props == null)
                props = new Dictionary<string, string>();
            if (flip.Sold)
                props["sold"] = "y";
            return this.SendFlip(new LowPricedAuction()
            {
                Auction = flip.Auction,
                DailyVolume = flip.Volume,
                Finder = flip.Finder,
                TargetPrice = flip.MedianPrice,
                AdditionalProps = props
            });
        }
    }
}
