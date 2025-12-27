using System;
using System.Collections.Generic;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using StackExchange.Redis;

#nullable enable

namespace Coflnet.Sky.Commands.MC
{
    public class SessionInfo : IDisposable, IPlayerInfo
    {
        /// <summary>
        /// The sessionId as set by the client
        /// </summary>
        public string clientSessionId = "";
        /// <summary>
        /// Client id identifying reconnects
        /// </summary>
        public string? clientConId;
        /// <summary>
        /// Cumputed session id out of the <see cref="clientSessionId"/> and the time rounded 
        /// </summary>
        public string SessionId = "";
        public bool ListeningToChat;
        public string McName { get; set; }
        public string McUuid { get; set; } = string.Empty;
        /// <summary>
        /// List of all minecraft uuids the user has verified
        /// </summary>
        public HashSet<string> MinecraftUuids = new();
        public DateTime LastMessage;
        public bool SentWelcome;

        [JsonIgnore]
        public ChannelMessageQueue? EventBrokerSub { get; internal set; }
        [JsonIgnore]
        public ChannelMessageQueue? EventBrokerUserSub { get; internal set; }

        /// <summary>
        /// Keeps track of which players a mute note message was already sent
        /// </summary>
        /// <returns></returns>
        public HashSet<string> SentMutedNoteFor { get; set; } = new();
        public bool LbinWarningSent { get; internal set; }

        public bool VerifiedMc;
        /// <summary>
        /// Wherever the user wants to receive flips or not 
        /// </summary>
        public bool FlipsEnabled;

        /// <summary>
        /// Random id for this connection to identify different ones
        /// </summary>
        public string ConnectionId = Guid.NewGuid().ToString();
        public TimeSpan RelativeSpeed = default;
        public DateTime LastBlockedMsg = default;
        public DateTime LastCaptchaSolve => captchaInfo.LastSolve;
        [JsonIgnore]
        public IEnumerable<string> CaptchaSolutions => captchaInfo.CurrentSolutions;
        public long Purse { get; set; }

        public bool IsIronman { get; internal set; }
        public bool IsBingo { get; internal set; }
        public bool IsStranded { get; internal set; }
        public bool IsDungeon { get; internal set; }
        public bool IsRift { get; internal set; }
        public bool IsMacroBot { get; set; }
        public bool IsDarkAuction { get; set; }
        public string? VerificationBidAuctioneer { get; set; }
        public int VerificationBidAmount { get; set; }
        [JsonIgnore]
        public CaptchaInfo captchaInfo = new();

        public bool IsNotFlipable => IsIronman || IsBingo || IsStranded || IsDungeon || IsRift || IsDarkAuction || Purse == -1;
        public string ConnectionType { get; set; }

        public int SkipLikeliness { get; set; }
        public int LicensePoints { get; set; }
        public string ActiveStream { get; set; }
        public bool IsDebug { get; set; }

        public AccountTier SessionTier { get; set; }
        /// <summary>
        /// Indicates whether the user owns the Rust Finder add-on (null = not checked yet)
        /// </summary>
        public bool? RustAddonOwned { get; set; }
        /// <summary>
        /// counts up for 100 not purchased flips
        /// </summary>
        public int NotPurchaseRate { get; set; }
        public bool NoSharedDelay { get; set; }
        public string ToLowListingAttempt { get; set; }
        public long AhSlotsOpen { get; set; } = -1;
        public int BestHotkeyUsageCount { get; set; }
        public string[] ModsFound { get; set; }
        public string ProfileId { get; set; }
        public SaveAuction SelectedItem { get; set; }
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

        public bool SellAll;

        public List<SaveAuction> Inventory { get; set; }
        public void Dispose()
        {
            EventBrokerSub?.Unsubscribe();
            EventBrokerUserSub?.Unsubscribe();
            EventBrokerSub = null;
            EventBrokerUserSub = null;
        }
    }

    public class CaptchaInfo
    {
        public IEnumerable<string> CurrentSolutions = new List<string>();
        public DateTime LastGenerated = default;
        public DateTime LastSolve = default;
        /// <summary>
        /// How many solves are requird, defaults to 1
        /// </summary>
        public int RequireSolves = 1;
        public int FaildCount = 0;
        /// <summary>
        /// How many captchas were requested so far
        /// </summary>
        public int CaptchaRequests = 0;
    }
}
