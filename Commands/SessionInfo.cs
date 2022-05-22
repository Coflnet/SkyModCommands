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
        public TimeSpan Penalty;

        public ChannelMessageQueue EventBrokerSub { get; internal set; }

        /// <summary>
        /// Keeps track of which players a mute note message was already sent
        /// </summary>
        /// <returns></returns>
        public HashSet<string> SentMutedNoteFor { get; set; } = new ();
        public int CaptchaFailedTimes { get; set; }

        public bool VerifiedMc;

        /// <summary>
        /// Random id for this connection to identify different ones
        /// </summary>
        public string ConnectionId = Guid.NewGuid().ToString();
        public TimeSpan RelativeSpeed = default;
        public DateTime LastSpeedUpdate = default;
        public DateTime LastBlockedMsg = default;
        public DateTime LastCaptchaSolve = default;
        public string CaptchaSolution = default;

        public void Dispose()
        {
            EventBrokerSub.Unsubscribe();
        }
    }
}
