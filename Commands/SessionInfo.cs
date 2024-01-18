using System;
using System.Collections.Generic;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.MC
{
    public class SessionInfo : IDisposable
    {
        /// <summary>
        /// The sessionId as set by the client
        /// </summary>
        public string clientSessionId = "";
        /// <summary>
        /// Cumputed session id out of the <see cref="clientSessionId"/> and the time rounded 
        /// </summary>
        public string SessionId = "";
        public bool ListeningToChat;
        public string McName;
        public string McUuid = string.Empty;
        /// <summary>
        /// List of all minecraft uuids the user has verified
        /// </summary>
        public HashSet<string> MinecraftUuids = new();
        public DateTime LastMessage;
        public bool SentWelcome;

        [JsonIgnore]
        public ChannelMessageQueue EventBrokerSub { get; internal set; }

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
        public long Purse { get; internal set; }

        public bool IsIronman { get; internal set; }
        public bool IsBingo { get; internal set; }
        public bool IsStranded { get; internal set; }
        public bool IsDungeon { get; internal set; }
        public bool IsRift { get; internal set; }
        public bool IsMacroBot { get; set; }
        public string VerificationBidAuctioneer { get; set; }
        [JsonIgnore]
        public CaptchaInfo captchaInfo = new();

        public bool IsNotFlipable => IsIronman || IsBingo || IsStranded || IsDungeon || IsRift;
        public string ConnectionType { get; set; }

        public List<SaveAuction> Inventory { get; set; }
        public bool SellAll;

        public void Dispose()
        {
            EventBrokerSub?.Unsubscribe();
            EventBrokerSub = null;
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
