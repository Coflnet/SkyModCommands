using System;

namespace Coflnet.Sky.Commands.MC
{
    public class SessionInfo
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
        public string McUuid = "00000000000000000";
        public DateTime LastMessage;
        public DateTime MutedUntil;
        public bool SentWelcome;
        /// <summary>
        /// Speed penalty for various bad actions eg botting
        /// </summary>
        public TimeSpan Penalty;
    }
}
