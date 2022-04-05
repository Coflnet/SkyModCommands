using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Helper;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Jaeger.Samplers;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;
using Microsoft.Extensions.DependencyInjection;
using Coflnet.Sky.ModCommands.Dialogs;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Main connection point for the mod.
    /// Handles establishing, authorization and handling of messages for a session
    /// </summary>
    public partial class MinecraftSocket : WebSocketBehavior, IFlipConnection
    {
        public static string COFLNET = "[§1C§6oflnet§f]§7: ";

        public long Id { get; private set; }

        public SessionInfo SessionInfo { get; protected set; } = new SessionInfo();

        public FlipSettings Settings => sessionLifesycle.FlipSettings;
        public SettingsChange LatestSettings => new SettingsChange()
        {
            Settings = sessionLifesycle.FlipSettings,
            Tier = sessionLifesycle.AccountInfo.Value.Tier,
            UserId = sessionLifesycle.AccountInfo.Value.UserId
        };

        public string Version { get; private set; }
        public OpenTracing.ITracer tracer = new Jaeger.Tracer.Builder("sky-commands-mod").WithSampler(new ConstSampler(true)).Build();
        public OpenTracing.ISpan ConSpan { get; private set; }
        public IModVersionAdapter ModAdapter;

        public FormatProvider formatProvider { get; private set; }
        public ModSessionLifesycle sessionLifesycle { get; private set; }



        public static ClassNameDictonary<McCommand> Commands = new ClassNameDictonary<McCommand>();

        public static event Action NextUpdateStart;

        int IFlipConnection.UserId => int.Parse(sessionLifesycle?.UserId ?? "0");
        public string UserId {
            get 
            {
                if(sessionLifesycle?.UserId == null)
                    throw new NullReferenceException("no user set");
                return sessionLifesycle.UserId.Value;
            }
        }

        private static System.Threading.Timer updateTimer;

        public ConcurrentQueue<BlockedElement> TopBlocked = new ConcurrentQueue<BlockedElement>();
        public ConcurrentQueue<LowPricedAuction> LastSent = new ConcurrentQueue<LowPricedAuction>();
        /// <summary>
        /// Triggered when the connection closes
        /// </summary>
        public event Action OnConClose;

        public class BlockedElement
        {
            public LowPricedAuction Flip;
            public string Reason;
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
            Commands.Add<GetCommand>();
            Commands.Add<TopUpCommand>();
            Commands.Add<HelpCommand>();
            Commands.Add<LogoutCommand>();

            Task.Run(async () =>
            {
                NextUpdateStart += () =>
                {
                    Console.WriteLine("next update");
                    GC.Collect();
                };
                DateTime next = await GetNextUpdateTime();
                Console.WriteLine($"started timer to start at {next} now its {DateTime.Now}");
                updateTimer = new System.Threading.Timer((e) =>
                {
                    try
                    {
                        NextUpdateStart?.Invoke();
                        if (DateTime.Now.Minute % 10 == 0)
                            UpdateTimer();
                    }
                    catch (Exception ex)
                    {
                        dev.Logger.Instance.Error(ex, "sending next update");
                    }
                }, null, next - DateTime.Now, TimeSpan.FromMinutes(1));
            }).ConfigureAwait(false);
        }

        private static void UpdateTimer()
        {
            Task.Run(async () =>
            {

                DateTime next = await GetNextUpdateTime();
                updateTimer.Change(next - DateTime.Now, TimeSpan.FromMinutes(1));
            });
        }

        private static async Task<DateTime> GetNextUpdateTime()
        {
            return await new NextUpdateRetriever().Get();
        }

        protected override void OnOpen()
        {
            ConSpan = tracer.BuildSpan("connection").Start();
            SendMessage(COFLNET + "§fNOTE §7This is a development preview, it is NOT stable/bugfree", $"https://discord.gg/wvKXfTgCfb", "Attempting to load your settings on " + System.Net.Dns.GetHostName());
            formatProvider = new FormatProvider(this);
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

            System.Console.CancelKeyPress += OnApplicationStop;

            NextUpdateStart -= SendTimer;
            NextUpdateStart += SendTimer;
        }

        private void StartConnection(OpenTracing.IScope openSpan)
        {
            var args = System.Web.HttpUtility.ParseQueryString(Context.RequestUri.Query);
            Console.WriteLine(Context.RequestUri.Query);
            if (args["uuid"] == null && args["player"] == null)
                Send(Response.Create("error", "the connection query string needs to include 'player'"));
            if (args["SId"] != null)
                SessionInfo.clientSessionId = args["SId"].Truncate(60);
            if (args["version"] != null)
                Version = args["version"].Truncate(10);

            ModAdapter = Version switch
            {
                "1.4-Alpha" => new InventoryVersionAdapter(this),
                "1.3-Alpha" => new ThirdVersionAdapter(this),
                "1.2-Alpha" => new SecondVersionAdapter(this),
                _ => new FirstModVersionAdapter(this)
            };

            var passedId = args["player"] ?? args["uuid"];
            TryAsyncTimes(async () => await LoadPlayerName(passedId), "loading PlayerName");
            ConSpan.SetTag("version", Version);

            string stringId;
            (this.Id, stringId) = ComputeConnectionId(passedId);
            ConSpan.SetTag("conId", stringId);

            FlipperService.Instance.AddNonConnection(this, false);
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    sessionLifesycle = Version switch
                    {
                        "1.4-Alpha" => new InventoryModSession(this),
                        _ => new ModSessionLifesycle(this)
                    };
                    await sessionLifesycle.SetupConnectionSettings(stringId);
                }
                catch (Exception e)
                {
                    Error(e, "failed to setup connection");
                    SendMessage(new DialogBuilder().CoflCommand<ReportCommand>("Whoops, we are very sorry but the connection setup failed. If this persists please click this message to create a report.", "failed to setup connection", "create a report"));
                }
            }).ConfigureAwait(false);
        }

        private Task TryAsyncTimes(Func<Task> action, string errorMessage, int times = 3)
        {
            return Task.Run(async () =>
            {
                for (int i = 0; i < times; i++)
                    try
                    {
                        await action();
                        return;
                    }
                    catch (System.Exception e)
                    {
                        Error(e, errorMessage);
                    }
            });
        }

        public async Task<string> GetPlayerName(string uuid)
        {
            return (await Shared.DiHandler.ServiceProvider.GetRequiredService<PlayerName.Client.Api.PlayerNameApi>()
                    .PlayerNameNameUuidGetAsync(uuid))?.Trim('"');
        }

        public T GetService<T>()
        {
            return Shared.DiHandler.ServiceProvider.GetRequiredService<T>();
        }


        private async Task LoadPlayerName(string passedId)
        {

            using var loadSpan = tracer.BuildSpan("nameLoad").AsChildOf(ConSpan).StartActive();
            var player = await PlayerService.Instance.GetPlayer(passedId);
            if (player == null)
            {
                var profile = await PlayerSearch.Instance.GetMcProfile(passedId);
                player = new Player() { Name = profile.Name, UuId = profile.Id };
                var update = await IndexerClient.TriggerNameUpdate(player.UuId);
            }
            SessionInfo.McName = player.Name;
            SessionInfo.McUuid = player.UuId;
            loadSpan.Span.SetTag("playerId", passedId);
            loadSpan.Span.SetTag("uuid", player.UuId);
        }


        protected (long, string) ComputeConnectionId(string passedId)
        {
            var bytes = Encoding.UTF8.GetBytes(passedId.ToLower() + SessionInfo.clientSessionId + DateTime.Now.RoundDown(TimeSpan.FromDays(14)).ToString());
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
            if (SessionInfo.clientSessionId.StartsWith("debug"))
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
                catch (CoflnetException e)
                {
                    SendMessage(COFLNET + $"{McColorCodes.RED}{e.Message}");
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
                    var auctionUuid = JsonConvert.DeserializeObject<string>(a.data).Trim('"').Replace("/viewauction ", "");
                    var flip = LastSent.Where(f => f.Auction.Uuid == auctionUuid).FirstOrDefault();
                    if (flip != null && flip.Auction.Context != null)
                        flip.AdditionalProps["clickT"] = (DateTime.Now - flip.Auction.FindTime).ToString();
                    await GetService<FlipTrackingService>().ClickFlip(auctionUuid, SessionInfo.McUuid);

                });
        }

        protected override void OnClose(CloseEventArgs e)
        {
            base.OnClose(e);
            FlipperService.Instance.RemoveConnection(this);
            ConSpan.Log(e?.Reason);

            ConSpan.Finish();
            OnConClose?.Invoke();
            sessionLifesycle.Dispose();
        }

        public new void Close()
        {
            base.Close();
            ConSpan.Log("force close");
            
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

        public OpenTracing.IScope RemoveMySelf()
        {
            var span = tracer.BuildSpan("removing").AsChildOf(ConSpan).StartActive();
            FlipperService.Instance.RemoveConnection(this);
            sessionLifesycle.Dispose();
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                span.Span.Finish();
            });
            return span;
        }

        public bool SendMessage(params ChatPart[] parts)
        {
            if (ConnectionState != WebSocketState.Open)
            {
                RemoveMySelf();
                ConSpan.Log("connection state was found to be " + ConnectionState);
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
            sessionLifesycle.Dispose();
            System.Console.CancelKeyPress -= OnApplicationStop;
            return span;
        }

        private void OnApplicationStop(object sender, ConsoleCancelEventArgs e)
        {
            SendMessage(COFLNET + "Server is restarting, you may experience connection issues for a few seconds.",
                 "/cofl start", "if it doesn't auto reconnect click this");
            System.Threading.Thread.Sleep(10);
        }

        public string Error(Exception exception, string message = null, string additionalLog = null)
        {
            using var error = tracer.BuildSpan("error").WithTag("message", message).WithTag("error", "true").StartActive();

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
            if (System.Net.Dns.GetHostName().Contains("ekwav"))
                Console.WriteLine(e.Message + "\n" + e.StackTrace);
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
                using var error = tracer.BuildSpan("error").WithTag("message", message).AsChildOf(ConSpan).WithTag("error", "true").StartActive();
            }
            ConSpan.Log(message);
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
                {
                    Log("con check was false");
                    return false;
                }
                await sessionLifesycle.SendFlip(flip);
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


        public string FormatPrice(long price)
        {
            if (Settings.ModSettings?.ShortNumbers ?? false)
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
            if (base.ConnectionState != WebSocketState.Open)
                return Task.FromResult(false);
            // don't send extra messages
            return Task.FromResult(true);
        }

        public void UpdateSettings(SettingsChange settings)
        {
        }

        public Task UpdateSettings(Func<SettingsChange, SettingsChange> updatingFunc)
        {
            return Task.CompletedTask;
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

        private void SendTimer()
        {
            using var loadSpan = tracer.BuildSpan("timer").AsChildOf(ConSpan).StartActive();
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
            if (Settings?.ModSettings?.PlaySoundOnFlip ?? false)
                SendSound("note.hat", 1);
        }

        public string FindWhatsNew(FlipSettings current, FlipSettings newSettings)
        {
            try
            {
                if (current.MinProfit != newSettings.MinProfit)
                    return "set min Profit to " + FormatPrice(newSettings.MinProfit);
                if (current.MinProfit != newSettings.MinProfit)
                    return "set max Cost to " + FormatPrice(newSettings.MaxCost);
                if (current.MinProfitPercent != newSettings.MinProfitPercent)
                    return "set min Profit percentage to " + FormatPrice(newSettings.MinProfitPercent);
                if (current.BlackList?.Count < newSettings.BlackList?.Count)
                    return $"blacklisted item " + ItemDetails.TagToName(newSettings.BlackList?.Last()?.ItemTag);
                if (current.WhiteList?.Count < newSettings.WhiteList?.Count)
                    return $"whitelisted item " + ItemDetails.TagToName(newSettings.BlackList?.Last()?.ItemTag);
                if (current.Visibility != null)
                    foreach (var prop in current.Visibility?.GetType().GetFields())
                    {
                        if (prop.FieldType == typeof(string))
                            return prop.Name + " changed";
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

        public async Task<bool> SendFlip(FlipInstance flip)
        {
            var props = flip.Context;
            if (props == null)
                props = new Dictionary<string, string>();
            if (flip.Sold)
                props["sold"] = "y";
            var result = await this.SendFlip(new LowPricedAuction()
            {
                Auction = flip.Auction,
                DailyVolume = flip.Volume,
                Finder = flip.Finder,
                TargetPrice = flip.MedianPrice,
                AdditionalProps = props
            });
            if (!result)
            {
                Log("failed");
                Log(base.ConnectionState.ToString());
            }


            return true;
        }
    }
}
