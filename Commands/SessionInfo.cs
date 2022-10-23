using System;
using System.Collections.Generic;
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
        public DateTime LastMessage;
        public DateTime MutedUntil;
        public bool SentWelcome;
        /// <summary>
        /// Speed penalty for various bad actions eg botting
        /// </summary>
        public TimeSpan Penalty = TimeSpan.FromSeconds(2);

        public ChannelMessageQueue EventBrokerSub { get; internal set; }

        /// <summary>
        /// Keeps track of which players a mute note message was already sent
        /// </summary>
        /// <returns></returns>
        public HashSet<string> SentMutedNoteFor { get; set; } = new();
        public int CaptchaFailedTimes => captchaInfo.RequireSolves;
        public bool LbinWarningSent { get; internal set; }

        public bool VerifiedMc;

        /// <summary>
        /// Random id for this connection to identify different ones
        /// </summary>
        public string ConnectionId = Guid.NewGuid().ToString();
        public TimeSpan RelativeSpeed = default;
        public DateTime LastSpeedUpdate = default;
        public DateTime LastBlockedMsg = default;
        public DateTime LastCaptchaSolve => captchaInfo.LastSolve;
        public IEnumerable<string> CaptchaSolutions => captchaInfo.CurrentSolutions;
        public CaptchaInfo captchaInfo = new();

        public void Dispose()
        {
            EventBrokerSub?.Unsubscribe();
            EventBrokerSub = null;
        }
    }

    public class CaptchaInfo
    {
        public IEnumerable<string> CurrentSolutions = new List<string>();
        public DateTime LastGenerated = DateTime.UtcNow;
        public DateTime LastSolve = default;
        /// <summary>
        /// How many solves are requird, defaults to 1
        /// </summary>
        public int RequireSolves = 1;
        /// <summary>
        /// How many captchas were requested so far
        /// </summary>
        public int CaptchaRequests = 0;
        public int ChatWidth = 90;
    }
}
